<#import "template.ftl" as layout>
<@layout.emailLayout>
    <h2>${msg("emailVerificationHello", user.firstName!'')}</h2>
    
    <p>${msg("emailVerificationBody", link, linkExpiration, realmName)}</p>
    
    <div style="text-align: center;">
        <a href="${link}" class="button">${msg("emailVerificationButton")}</a>
    </div>
    
    <p class="info-text">${msg("emailVerificationLinkExpiry", linkExpiration)}</p>
    
    <div class="link-fallback">
        <strong>${msg("emailVerificationLinkFallback")}</strong><br>
        ${link}
    </div>
    
    <p class="info-text">${msg("emailVerificationIgnore")}</p>
</@layout.emailLayout>
