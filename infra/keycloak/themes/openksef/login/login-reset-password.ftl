<#import "template.ftl" as layout>
<@layout.registrationLayout displayInfo=true displayMessage=!messagesPerField.existsError('username'); section>
    <#if section = "header">
        Nie pamiętasz hasła?
    <#elseif section = "desc">
        Podaj email konta. Wyślemy link do ustawienia nowego hasła.
    <#elseif section = "form">
        <form id="kc-reset-password-form" class="auth-card__form" action="${url.loginAction}" method="post">
            <@layout.field id="username" name="username" type="email" label="Email" placeholder="jan@firma.pl" value=((auth.attemptedUsername)!'') autocomplete="username" error=messagesPerField.existsError('username')?then('Uzupełnij to pole.', '')/>
            <@layout.primary text="Wyślij link" pending="Wysyłanie..."/>
        </form>
    <#elseif section = "info">
        <@layout.footer "Pamiętasz hasło?" "Wróć do logowania" url.loginUrl/>
    </#if>
</@layout.registrationLayout>
