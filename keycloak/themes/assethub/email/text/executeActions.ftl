${msg("executeActionsHello", user.firstName!'')}

${msg("executeActionsBodyHtml")}

<#if requiredActions?has_content>
${msg("executeActionsRequired")}
<#list requiredActions as action>
- ${msg("requiredAction.${action}")}
</#list>
</#if>

${msg("executeActionsButton")}: ${link}

${msg("executeActionsLinkExpiry", linkExpiration)}

${msg("executeActionsIgnore")}
