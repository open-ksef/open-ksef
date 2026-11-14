<#import "template.ftl" as layout>
<#assign reauth = auth?has_content && auth.showUsername() && !auth.showResetCredentials()>
<#assign device = (client.clientId!"") == "openksef-mobile">
<@layout.registrationLayout displayMessage=false displayInfo=true brandH1=true pageTitle="Logowanie"; section>
    <#if section = "form">
        <p class="auth-card__desc"><#if reauth>Zaloguj się ponownie, aby kontynuować<#else>Zaloguj się do portalu OpenKSeF</#if></p>
        <#if device && !reauth><div data-pairing-notice hidden><@layout.notice "info" "smartphone">Logujesz telefon. Po zalogowaniu potwierdzisz kod z aplikacji.</@layout.notice></div></#if>
        <#if messagesPerField.existsError('username','password')>
            <@layout.formError>${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}</@layout.formError>
        <#elseif message?has_content && message.summary == msg("emailSentMessage")>
            <@layout.notice "success" "mail">Jeśli konto z tym adresem istnieje, wysłaliśmy na niego link do ustawienia nowego hasła.</@layout.notice>
        <#elseif message?has_content && message.type == "error" && ["identityProviderAuthenticationFailedMessage", "identityProviderUnexpectedErrorMessage", "identityProviderMissingStateMessage", "identityProviderMissingCodeOrErrorMessage", "identityProviderInvalidResponseMessage", "identityProviderInvalidSignatureMessage", "identityProviderNotFoundMessage", "identityProviderLoginFailureMessage"]?filter(k -> msg(k) == message.summary)?has_content>
            <@layout.formError>Nie udało się zalogować przez Google. Spróbuj ponownie albo zaloguj się emailem i hasłem.</@layout.formError>
        <#elseif message?has_content && message.type == "error">
            <@layout.formError>${kcSanitize(message.summary)?no_esc}</@layout.formError>
        <#elseif message?has_content && message.type == "success">
            <@layout.notice message.type>${kcSanitize(message.summary)?no_esc}</@layout.notice>
        </#if>
        <#if realm.password>
            <form id="kc-form-login" class="auth-card__form" action="${url.loginAction}" method="post">
                <#if reauth || usernameHidden??>
                    <div class="auth-user"><span>${auth.attemptedUsername}</span><a href="${url.loginRestartFlowUrl}">Zmień konto</a></div>
                <#else>
                    <@layout.field id="username" name="username" label="Email" placeholder="jan@firma.pl" value=(login.username!'') autocomplete="username"/>
                </#if>
                <#if realm.resetPasswordAllowed>
                    <@layout.field id="password" name="password" type="password" label="Hasło" placeholder="••••••••" autocomplete="current-password" labelLink=url.loginResetCredentialsUrl labelLinkText="Nie pamiętasz hasła?"/>
                <#else>
                    <@layout.field id="password" name="password" type="password" label="Hasło" placeholder="••••••••" autocomplete="current-password"/>
                </#if>
                <input type="hidden" name="credentialId" value="${(auth.selectedCredential)!''}">
                <@layout.primary text="Zaloguj się" pending="Logowanie..." name="login" value="login"/>
            </form>
        </#if>
    <#elseif section = "socialProviders">
        <#if realm.password && !reauth><@layout.google "Zaloguj przez Google"/></#if>
    <#elseif section = "info">
        <#if realm.password && realm.registrationAllowed && !registrationDisabled?? && !reauth && !device>
            <@layout.footer "Nie masz konta?" "Zarejestruj się" url.registrationUrl/>
        </#if>
    </#if>
</@layout.registrationLayout>
