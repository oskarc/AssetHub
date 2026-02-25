<#import "template.ftl" as layout>
<@layout.emailLayout>
    <h2>${msg("executeActionsHello", user.firstName!'')}</h2>
    
    <p>${msg("executeActionsBodyHtml", link, linkExpiration, realmName, linkExpirationFormatter(linkExpiration))}</p>
    
    <#if requiredActions?has_content>
    <p>${msg("executeActionsRequired")}</p>
    <ul>
        <#list requiredActions as action>
        <li>${msg("requiredAction.${action}")}</li>
        </#list>
    </ul>
    </#if>
    
    <div style="text-align: center;">
        <a href="${link}" class="button">${msg("executeActionsButton")}</a>
    </div>
    
    <p class="info-text">${msg("executeActionsLinkExpiry", linkExpiration)}</p>
    
    <div class="link-fallback">
        <strong>${msg("executeActionsLinkFallback")}</strong><br>
        ${link}
    </div>
    
    <p class="info-text">${msg("executeActionsIgnore")}</p>
</@layout.emailLayout>
