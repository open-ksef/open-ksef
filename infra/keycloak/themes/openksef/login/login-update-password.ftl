<#import "template.ftl" as layout>
<#assign aia = isAppInitiatedAction??>
<#assign account = username!((auth.attemptedUsername)!'')>
<@layout.registrationLayout displayMessage=false; section>
    <#if section = "header">
        <#if aia>Zmień hasło<#else>Ustaw nowe hasło</#if>
    <#elseif section = "desc">
        <#if aia>Po zapisaniu wrócisz do Ustawień.<#elseif account?has_content>Konto ${account}</#if>
    <#elseif section = "form">
        <#if message?has_content && message.type == "error" && !messagesPerField.existsError('password','password-confirm')>
            <@layout.formError>${kcSanitize(message.summary)?no_esc}</@layout.formError>
        </#if>
        <form id="kc-passwd-update-form" class="auth-card__form" action="${url.loginAction}" method="post">
            <input type="hidden" name="username" value="${account}" autocomplete="username">
            <@layout.field id="password-new" name="password-new" type="password" label="Nowe hasło" placeholder="Min. 8 znaków" autocomplete="new-password" error=layout.fieldError('password')/>
            <@layout.field id="password-confirm" name="password-confirm" type="password" label="Powtórz nowe hasło" placeholder="Powtórz hasło" autocomplete="new-password" error=layout.fieldError('password-confirm')/>
            <label class="auth-check"><input type="checkbox" id="logout-sessions" name="logout-sessions" value="on" checked> Wyloguj mnie na innych urządzeniach</label>
            <@layout.primary text="Zapisz hasło" pending="Zapisywanie..."/>
            <#if aia><@layout.secondary text="Anuluj" name="cancel-aia" value="true"/></#if>
        </form>
    </#if>
</@layout.registrationLayout>
