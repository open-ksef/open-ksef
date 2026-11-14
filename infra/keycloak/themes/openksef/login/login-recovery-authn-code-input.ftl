<#import "template.ftl" as layout>
<#assign n = recoveryAuthnCodesInputBean.codeNumber?c>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('recoveryCodeInput') icon="key-round"; section>
    <#if section = "header">
        Kod zapasowy
    <#elseif section = "desc">
        Wpisz kod nr ${n} z listy, którą zapisałeś przy włączaniu weryfikacji.
    <#elseif section = "form">
        <#if messagesPerField.existsError('recoveryCodeInput')>
            <@layout.formError>Nieprawidłowy kod zapasowy. Sprawdź, czy to kod nr ${n}.</@layout.formError>
        </#if>
        <form id="kc-recovery-code-login-form" class="auth-card__form" action="${url.loginAction}" method="post">
            <@layout.codeField id="recoveryCodeInput" name="recoveryCodeInput" label="Kod zapasowy nr ${n}" placeholder="XXXX-XXXX-XXXX"/>
            <@layout.primary text="Zaloguj się" pending="Logowanie..." name="login" value="login"/>
        </form>
        <@layout.tryAnotherWay "Wróć do kodu z aplikacji"/>
        <p class="auth-card__subdesc">Nie masz ani telefonu, ani kodów? Poproś administratora OpenKSeF o reset weryfikacji.</p>
    </#if>
</@layout.registrationLayout>
