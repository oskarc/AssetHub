<#import "template.ftl" as layout>
<@layout.emailLayout>
    <h2>${msg("passwordResetHello", user.firstName!'')}</h2>
    
    <p>${msg("passwordResetBody", link, linkExpiration, realmName)}</p>
    
    <div style="text-align: center;">
        <a href="${link}" class="button">${msg("passwordResetButton")}</a>
    </div>
    
    <p class="info-text">${msg("passwordResetLinkExpiry", linkExpiration)}</p>
    
    <div class="link-fallback">
        <strong>${msg("passwordResetLinkFallback")}</strong><br>
        ${link}
    </div>
    
    <p class="info-text">${msg("passwordResetIgnore")}</p>
</@layout.emailLayout>
