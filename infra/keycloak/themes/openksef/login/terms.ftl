<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false; section>
    <#if section = "header">
        Regulamin
    <#elseif section = "form">
        <div class="auth-terms" tabindex="0" role="region" aria-label="Treść regulaminu">${kcSanitize(msg("termsText"))?no_esc}</div>
        <form class="auth-card__actions" action="${url.loginAction}" method="post">
            <@layout.primary text="Akceptuję" name="accept" value="accept"/>
            <@layout.secondary text="Nie akceptuję" name="cancel" value="cancel"/>
        </form>
    </#if>
</@layout.registrationLayout>
