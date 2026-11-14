<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false displayInfo=true icon="mail"; section>
    <#if section = "header">
        Potwierdź adres email
    <#elseif section = "form">
        <p class="auth-card__text">Wysłaliśmy link na adres <strong>${verifyEmail!((user.email)!'')}</strong>. Kliknij go, aby aktywować konto.</p>
        <div data-resent-notice hidden><@layout.notice "success" "check">Wysłaliśmy nowy link. Sprawdź też folder Spam.</@layout.notice></div>
        <#if message?has_content && message.type == "error">
            <@layout.formError>${kcSanitize(message.summary)?no_esc}</@layout.formError>
        </#if>
        <#if isAppInitiatedAction??>
            <form id="kc-verify-email-form" class="auth-card__actions" action="${url.loginAction}" method="post">
                <@layout.secondary text="Wyślij link ponownie"/>
                <@layout.secondary text="Anuluj" name="cancel-aia" value="true"/>
            </form>
        <#else>
            <div class="auth-card__actions" data-resend><@layout.secondary text="Wyślij link ponownie" href=url.loginAction/></div>
        </#if>
    <#elseif section = "info">
        <@layout.footer "Zły adres?" "Wróć do logowania" url.loginRestartFlowUrl/>
    </#if>
</@layout.registrationLayout>
