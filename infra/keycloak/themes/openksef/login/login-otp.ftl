<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('totp') displayInfo=true icon="shield-check" pageTitle="Weryfikacja dwuetapowa"; section>
    <#if section = "header">
        Weryfikacja dwuetapowa
    <#elseif section = "desc">
        Wpisz 6-cyfrowy kod z aplikacji uwierzytelniającej.
    <#elseif section = "form">
        <#if messagesPerField.existsError('totp')>
            <@layout.formError>Nieprawidłowy kod. Sprawdź, czy telefon ma automatyczny czas, i wpisz nowy kod.</@layout.formError>
        </#if>
        <form id="kc-otp-login-form" class="auth-card__form" action="${url.loginAction}" method="post">
            <input type="hidden" name="selectedCredentialId" value="${otpLogin.selectedCredentialId!''}">
            <@layout.codeField id="otp" name="otp" label="Kod z aplikacji" placeholder="000000" numeric=true/>
            <@layout.primary text="Potwierdź" pending="Sprawdzanie..." name="login" value="login"/>
        </form>
        <@layout.tryAnotherWay "Nie masz telefonu? Użyj kodu zapasowego"/>
    <#elseif section = "info">
        <@layout.footer "To nie Ty?" "Wróć do logowania" url.loginRestartFlowUrl/>
    </#if>
</@layout.registrationLayout>
