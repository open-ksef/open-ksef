<#import "template.ftl" as layout>
<#assign mobile = client.clientId == "openksef-mobile">
<#assign account = (user.email)!((auth.attemptedUsername)!"")>
<@layout.registrationLayout displayMessage=false icon="smartphone" iconKind="warning"; section>
    <#if section = "header">
        <#if mobile>Zalogować telefon?<#else>Zezwolić na dostęp: ${advancedMsg(client.name!client.clientId)}?</#if>
    <#elseif section = "desc">
        <#if account?has_content>Konto ${account}</#if>
    <#elseif section = "form">
        <#if mobile>
            <p class="auth-card__text auth-card__text--left">Zatwierdź tylko, jeśli ten kod widzisz teraz na swoim telefonie. Jeśli ktoś podał Ci kod lub link, wybierz Nie.</p>
        </#if>
        <p class="auth-card__text auth-card__text--left"><strong><#if mobile>Telefon dostanie:<#else>Aplikacja dostanie:</#if></strong></p>
        <ul class="auth-scopes">
            <#if mobile>
                <li><@layout.svg "check"/><span>dostęp do Twoich firm i faktur, także do wystawiania faktur w KSeF,</span></li>
                <li><@layout.svg "check"/><span>logowanie bez hasła do roku albo do wylogowania.</span></li>
            <#elseif oauth.clientScopesRequested??>
                <#list oauth.clientScopesRequested as scope>
                    <li><@layout.svg "check"/><span>${advancedMsg(scope.consentScreenText)}<#if scope.parameterizedScopeParameter??>: <b>${scope.parameterizedScopeParameter}</b></#if></span></li>
                </#list>
            </#if>
        </ul>
        <form class="auth-card__actions" action="${url.oauthAction}" method="post">
            <input type="hidden" name="code" value="${oauth.code}">
            <@layout.primary text=mobile?then("Tak, zaloguj telefon", "Zezwól") name="accept" value="accept"/>
            <@layout.secondary text="Nie" name="cancel" value="cancel"/>
        </form>
    </#if>
</@layout.registrationLayout>
