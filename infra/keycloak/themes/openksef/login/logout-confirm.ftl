<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false; section>
    <#if section = "header">
        Wylogować się?
    <#elseif section = "form">
        <p class="auth-card__text">Wylogujesz się z OpenKSeF w tej przeglądarce.</p>
        <form class="auth-card__actions" action="${url.logoutConfirmAction}" method="post">
            <input type="hidden" name="session_code" value="${logoutConfirm.code}">
            <@layout.primary text="Wyloguj" pending="Wylogowywanie..." name="confirmLogout" value="true"/>
        </form>
        <@layout.link "Wróć do OpenKSeF" "/"/>
    </#if>
</@layout.registrationLayout>
