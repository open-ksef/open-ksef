<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false icon="clock" iconKind="warning"; section>
    <#if section = "header">
        Strona wygasła
    <#elseif section = "form">
        <p class="auth-card__text">Logowanie trwało za długo. Zacznij od nowa.</p>
        <div class="auth-card__actions"><@layout.primaryLink "Zaloguj się ponownie" url.loginRestartFlowUrl/></div>
    </#if>
</@layout.registrationLayout>
