---
name: pattern-hash-keyed-pii-reveal
description: When a feature needs to (a) group / index over PII without storing it in the clear and (b) reveal the plaintext on explicit admin action. Store an HMAC hash for grouping + a DataProtection-encrypted ciphertext for the plaintext; reveal endpoint decrypts on demand and audits hash + outcome only — never the decrypted value. Use for forensic attribution, exposure analytics, recipient-by-something dashboards, anywhere "who did what" is the question and PII is the answer.
---

# Hash-keyed PII with audited reveal

A common shape in compliance- or forensic-adjacent features: you need to group / count / index over personal data (a recipient email, a Keycloak user-id, a phone number) without that data sitting in the clear, but you also need to be able to *reveal* the plaintext when an admin asks "who is recipient X?". The naive approach — store plaintext, audit access — leaks PII into the audit log itself. The naive-but-paranoid approach — never store decryptable plaintext — kills the legitimate forensic use case.

This pattern carries both: indexable hash + reversibly-encrypted ciphertext + reveal endpoint that audits the *attempt*, not the *answer*.

Announce in chat whether you're running this skill or skipping it, and why.

## When to run

Reach for this pattern when **all** of:
- The feature needs to group, count, or look up by a piece of PII (email, user-id, phone, address-token, anything regulated).
- A legitimate role (admin, compliance, security) needs to read the plaintext on demand.
- The audit log is itself a security-sensitive surface — leaking PII *into the audit row* defeats the purpose.

Skip for:
- Pure UI personalisation. ("Show the user their own data" doesn't need this — they already have the plaintext.)
- Features where the plaintext never needs to be revealed. Use one-way HMAC hashing alone; skip the encrypted column.
- Features where every consumer can see plaintext anyway. The audit-without-PII property is the reason this pattern exists; if every read can dump the value, you don't gain anything from the dance.

## Principles (why)

### 1. The audit log records *that* a reveal happened, not *what* was revealed.

This is the load-bearing rule. An audit log that captures decrypted PII isn't an audit log — it's a second copy of the data, with weaker access control than the source. Every reveal-flow audit row must be reviewable in a "what was accessed when" sense without leaking the *content* of the access.

Concrete corollary: the test for this feature is to serialise the audit row and assert the plaintext substring is **not** present. If a reviewer can read the audit log and reconstruct the PII, the rule is broken.

### 2. Hash for grouping, encrypt for storage, decrypt only on demand.

Three keyed operations doing three different jobs:
- **Hash** (HMAC): indexable, deterministic, irreversible. Lets you GROUP BY and JOIN over PII without ever holding plaintext.
- **Encrypt** (DataProtection / KMS / equivalent): ciphertext at rest, decryptable only via the active key. Stores the plaintext for legitimate reveal.
- **Decrypt**: only ever on the explicit reveal path. Never on a list / aggregate / dashboard read.

Each operation has a separate failure mode. Conflating "the data" with "any one of these three views of it" leads to bugs where plaintext leaks through a column that should have been ciphertext.

### 3. Probe-resistance: don't audit reveals for hashes that don't exist.

Counter-intuitive but critical. If your reveal endpoint audits every attempt, including misses, the audit log becomes a hash-fishing oracle: an attacker can submit guessed hashes and watch the audit log to enumerate which ones are valid.

Audit on **success or decryption-attempted-but-failed**, not on **404 / hash-not-in-table**. Treat the not-found path as a normal read; audit only the reveal flow proper.

### 4. Key rotation tolerance is a contract, not an emergency.

Encryption keys rotate. When they do, historical ciphertext becomes unreadable. The reveal endpoint must report this cleanly:
- Decrypt failure returns `null`, not throws.
- The endpoint surfaces a typed `decryption_failed = true` flag to the caller.
- The audit row records the hash + the failure outcome — auditors need to see "admin tried to reveal X, key rotation made it unrecoverable" as much as the success case.

The hash key is a separate question — it must NOT rotate, otherwise historical hashes desynchronise from current ones and grouping breaks.

## Patterns (what)

### The schema

For each PII column you want to handle:

```
EntityTable {
  ...
  EncryptedX     (bytea)        -- DataProtection ciphertext; null when no value
  XHash          (bytea[32])    -- HMAC-SHA256(plaintext); null when no value; indexed
  ...
  Index (XHash)
}
```

If you have multiple kinds of recipient (e.g. user-id and email), each gets its own pair of columns + its own discriminator on the reveal endpoint:

```
EncryptedRecipientUserId, RecipientUserIdHash
EncryptedRecipientEmail,  RecipientEmailHash
```

The discriminator (`"user"` / `"email"`) tells the reveal endpoint which pair to read.

### The crypto service

Three methods, one job each:

```
interface IRecipientCrypto {
  byte[] Encrypt(string plaintext);   // DataProtection.Protect — ciphertext for storage
  string? Decrypt(byte[] cipher);     // DataProtection.Unprotect — null on key-rotation failure
  byte[] Hash(string plaintext);      // HMAC-SHA256 with a deployment-stable key
}
```

The encrypt key comes from a rotatable provider (cert-wrapped DataProtection, KMS, etc.). The hash key comes from a deployment-scoped secret that must NOT rotate during the lifetime of the data — usually a base64 32-byte value seeded once per environment.

`Decrypt` returning `null` on failure (not throwing) is what makes the reveal endpoint's failure path tractable. A throwing decrypt poisons the whole audit-flow with try/catch noise.

### The reveal endpoint

```
POST /admin/.../reveal
Body: { recipientHash, recipientKind }
```

Steps:
1. Validate hash is well-formed (base64, expected length). On bad input → 400, no audit.
2. Look up a row by `(recipientKind hash column == hash)`. If nothing → 404, no audit. (See Principle 3.)
3. Decrypt the encrypted column. May return null on key rotation.
4. Audit `{event: reveal, target: feature, details: {recipient_hash, recipient_kind, decryption_failed}}`. **Never include the decrypted plaintext.**
5. Return the plaintext (or `decryption_failed = true`) to the admin.

The order of (3) and (4) matters: audit AFTER decrypt-attempt so the row records the actual outcome, not just "we got here."

### The list / dashboard read

The list endpoint returns hashes, never plaintext. Each row carries enough context (count, distinct-asset count, kind discriminator) to be useful at the aggregate level. The UI displays a truncated hash + a Reveal button; clicking it hits the reveal endpoint.

Don't pre-decrypt "if we're already an admin" — that defeats the audit. Decrypt is *only* on the reveal path.

### The test that holds the line

Every implementation of this pattern needs one specific test:

```
// Arrange — encrypt some plaintext, store it with a known hash.
// Act — call reveal.
// Assert — reveal returned the plaintext.
// Assert — the audit row's serialised details DO NOT contain the plaintext substring.
```

The last assertion is the load-bearing one. It catches:
- A future maintainer adding `["recipient"] = plaintext` to the details dictionary.
- A subtle bug where the hash format coincidentally embeds plaintext bytes.
- A logger.LogInformation that thinks audit-detail-Dump is helpful.

If this test isn't there, the pattern's central guarantee is unverified.

## Implementation constraints (how)

- **Hash algorithm**: HMAC-SHA256. Plain SHA-256 over PII is a rainbow-table target; HMAC with a deployment-scoped key is not.
- **Hash key longevity**: rotate never (or via an explicit migration that re-hashes every row). If the hash key rotates silently, GROUP BY over historical data desynchronises.
- **Encryption mechanism**: per-platform — DataProtection (.NET), KMS-envelope (AWS), Cloud KMS (GCP), etc. The pattern is mechanism-neutral; the failure modes (key rotation tolerance, returns-null-not-throws) are the contract.
- **Audit retention**: reveal-event audit rows have the same retention as the source feature's audit rows. The reveal log is *security-relevant*, not chatty — don't aggressively prune it.
- **List-endpoint authorisation**: the list (group-by-hash) endpoint should already be admin-only. The reveal endpoint inherits that, plus often adds a stricter scope (e.g. `RequireScopeFilter("admin")` on a PAT context). Hash-only output without admin auth is still a probe surface.

## Anti-patterns to avoid

- **Storing plaintext PII in the audit log**. The whole pattern exists to prevent this. Every audit-row construction near a reveal flow needs a plaintext-substring lint or a code-review gate.
- **Logging the decrypted plaintext for "debugging"**. The same `Decrypt(...)` call that drives the reveal must not also feed `logger.LogInformation`. If you need debug visibility, log the hash + the decryption-success boolean — never the value.
- **Pre-decrypting on the list endpoint**. "Admins can see anyway" leaks PII into the dashboard render path, which then leaks into telemetry, browser history, support screenshots. Reveal stays per-row, on-demand.
- **Auditing 404s on the reveal path**. Turns the audit log into a hash-validity oracle (Principle 3).
- **Throwing on decrypt failure**. Forces every caller into try/catch and obscures the legitimate "key rotated, this row is unreadable" outcome.
- **Sharing one key across hash + encrypt**. Two different jobs, two different rotation profiles, two different keys.
- **Skipping the test that asserts plaintext is not in the audit row**. Without it, the pattern's central guarantee is unverified — and the next refactor will quietly break it.
