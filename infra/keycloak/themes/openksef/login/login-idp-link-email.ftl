<#import "template.ftl" as layout>
<@layout.registrationLayout icon="mail"; section>
    <#if section = "header">
        Potwierdź połączenie kont
    <#elseif section = "form">
        <p class="auth-card__text">Wysłaliśmy link na <strong>${(brokerContext.email)!((brokerContext.username)!'')}</strong>. Kliknij go, aby połączyć konto z Google.</p>
        <div class="auth-card__actions"><@layout.secondary text="Wyślij link ponownie" href=url.loginAction/></div>
        <@layout.tryAnotherWay "Połącz hasłem zamiast emailem"/>
    </#if>
</@layout.registrationLayout>
