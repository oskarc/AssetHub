---
description: "Clean Architecture guidelines for AssetHub"
applyTo: '**/*.cs,**/*.csproj,**/Program.cs,**/*.razor'
---

# Architecture — AssetHub Overrides

These rules override generic DDD/architecture guidance for the AssetHub project.

## AssetHub Architecture: Clean Architecture (not strict DDD)

AssetHub follows **Clean Architecture** with these layers:
- **Domain**: Entities, enums — no base classes, no value objects, no domain events, no specifications.
- **Application**: Service interfaces, **repository interfaces**, DTOs, configuration, `ServiceResult<T>`.
- **Infrastructure**: EF Core implementations, external service adapters, Polly resilience.
- **Api / Worker**: Composition roots — DI wiring, endpoint mapping, auth configuration.
- **Ui**: Blazor Server (Razor Class Library) — depends only on Application.

## What AssetHub Does NOT Use

Do not generate code using these patterns — they are not part of this codebase:
- **Domain events** — use Wolverine messages for async processing instead.
- **Value objects** — use primitive types or simple classes.
- **Specifications pattern** — use LINQ queries in repositories directly.
- **Rich domain models** — entities are simple with minimal behavior (only `Asset` has state methods).
- **Aggregate root pattern** — entities are standalone, no aggregate boundaries.
- **Event sourcing** — use standard EF Core persistence.

## Key Differences from Generic DDD Guidance

| Topic | Generic DDD Guidance | AssetHub Reality |
|-------|---------------------|-----------------|
| Repository interfaces | Domain layer | **Application layer** (`Application/Repositories/`) |
| Business logic | Rich domain models | **Service layer** (`Infrastructure/Services/`) returning `ServiceResult<T>` |
| Error handling | Domain exceptions | **`ServiceResult<T>`** — never throw for business errors |
| Cross-aggregate communication | Domain events | **Wolverine messages** via RabbitMQ |
| Test coverage | 85% minimum | No stated minimum — focus on critical paths |

## SOLID Principles Still Apply

The generic guidance on SOLID principles remains valid:
- **Single Responsibility** — services split by concern (commands, queries, uploads).
- **Dependency Inversion** — interfaces in Application, implementations in Infrastructure.
- **Interface Segregation** — separate query and command service interfaces.

## Testing Standards Still Apply

- **Test naming**: `MethodName_Condition_ExpectedResult`.
- **Unit tests** for service logic and domain rules.
- **Integration tests** for repositories and endpoints.
- **xUnit + Moq + Testcontainers** — not NUnit or MSTest.

## Quality Checklist

**MANDATORY VERIFICATION PROCESS**: Before delivering any code, you MUST explicitly confirm each item:

### Domain Design Validation

* **Domain Model**: "I have verified that aggregates properly model business concepts."
* **Ubiquitous Language**: "I have confirmed consistent terminology throughout the codebase."
* **SOLID Principles Adherence**: "I have verified the design follows SOLID principles."
* **Business Rules**: "I have validated that domain logic is encapsulated in aggregates."
* **Event Handling**: "I have confirmed domain events are properly published and handled."

### Implementation Quality Validation

* **Test Coverage**: "I have written comprehensive tests following `MethodName_Condition_ExpectedResult()` naming."
* **Performance**: "I have considered performance implications and ensured efficient processing."
* **Security**: "I have implemented authorization at aggregate boundaries."
* **Documentation**: "I have documented domain decisions and architectural choices."
* **.NET Best Practices**: "I have followed .NET best practices for async, DI, and error handling."

### Financial Domain Validation

* **Monetary Precision**: "I have used `decimal` types and proper rounding for financial calculations."
* **Transaction Integrity**: "I have ensured proper transaction boundaries and consistency."
* **Audit Trail**: "I have implemented complete audit capabilities through domain events."
* **Compliance**: "I have addressed PCI-DSS, SOX, and LGPD requirements."

**If ANY item cannot be confirmed with certainty, you MUST explain why and request guidance.**

### Monetary Values

* Use `decimal` type for all monetary calculations.
* Implement currency-aware value objects.
* Handle rounding according to financial standards.
* Maintain precision throughout calculation chains.

### Transaction Processing

* Implement proper saga patterns for distributed transactions.
* Use domain events for eventual consistency.
* Maintain strong consistency within aggregate boundaries.
* Implement compensation patterns for rollback scenarios.

### Audit and Compliance

* Capture all financial operations as domain events.
* Implement immutable audit trails.
* Design aggregates to support regulatory reporting.
* Maintain data lineage for compliance audits.

### Financial Calculations

* Encapsulate calculation logic in domain services.
* Implement proper validation for financial rules.
* Use specifications for complex business criteria.
* Maintain calculation history for audit purposes.

### Platform Integration

* Use system standard DDD libraries and frameworks.
* Implement proper bounded context integration.
* Maintain backward compatibility in public contracts.
* Use domain events for cross-context communication.

**Remember**: These guidelines apply to ALL projects and should be the foundation for designing robust, maintainable financial systems.

## CRITICAL REMINDERS

**YOU MUST ALWAYS:**

* Show your thinking process before implementing.
* Explicitly validate against these guidelines.
* Use the mandatory verification statements.
* Follow the `MethodName_Condition_ExpectedResult()` test naming pattern.
* Confirm financial domain considerations are addressed.
* Stop and ask for clarification if any guideline is unclear.

**FAILURE TO FOLLOW THIS PROCESS IS UNACCEPTABLE** - The user expects rigorous adherence to these guidelines and code standards.
