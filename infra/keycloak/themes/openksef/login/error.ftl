<#import "template.ftl" as layout>
<#assign s = (message.summary)!"">
<#function is keys...>
    <#list keys as k><#if s == msg(k)><#return true></#if></#list>
    <#return false>
</#function>
<#assign restart = "/login">
<#if is("expiredActionMessage", "expiredActionTokenNoSessionMessage", "expiredActionTokenSessionExistsMessage", "invalidCodeMessage", "expiredCodeMessage", "staleCodeMessage")>
    <#assign e = ["error", "clock", "Link wygasł", "Ten link jest już nieważny. Poproś o nowy na stronie logowania.", "Wróć do logowania", restart]>
<#elseif is("accountDisabledMessage")>
    <#assign e = ["error", "lock", "Konto wyłączone", "Nie możesz się zalogować. Skontaktuj się z administratorem OpenKSeF w swojej firmie.", "Wróć do OpenKSeF", "/"]>
<#elseif is("cookieNotFoundMessage")>
    <#assign e = ["warning", "circle-alert", "Włącz pliki cookie", "Logowanie wymaga plików cookie. Włącz je w przeglądarce i spróbuj ponownie.", "Spróbuj ponownie", "/"]>
<#elseif is("identityProviderAuthenticationFailedMessage", "identityProviderUnexpectedErrorMessage", "identityProviderMissingStateMessage", "identityProviderMissingCodeOrErrorMessage", "identityProviderInvalidResponseMessage", "identityProviderInvalidSignatureMessage", "identityProviderNotFoundMessage")>
    <#assign e = ["error", "circle-x", "Nie udało się zalogować przez Google", "Spróbuj ponownie albo zaloguj się emailem i hasłem.", "Wróć do logowania", restart]>
<#elseif is("oauth2DeviceAuthorizationGrantDisabledMessage")>
    <#assign e = ["error", "circle-x", "Parowanie niedostępne", "Ten serwer nie obsługuje parowania kodem. Zaloguj się w aplikacji emailem i hasłem.", "Wróć do OpenKSeF", "/"]>
<#elseif is("oauth2DeviceConsentDeniedMessage", "oauth2DeviceVerificationFailedMessage", "consentDenied")>
    <#assign e = ["error", "circle-x", "Nie zalogowano telefonu", "Odrzuciłeś logowanie. Jeśli to pomyłka, poproś w aplikacji o nowy kod.", "", "/"]>
<#else>
    <#assign e = ["error", "circle-alert", "Coś poszło nie tak", "Spróbuj ponownie za chwilę. Jeśli błąd wraca, napisz do administratora OpenKSeF.", "Wróć do OpenKSeF", "/"]>
</#if>
<@layout.registrationLayout displayMessage=false icon=e[1] iconKind=e[0]; section>
    <#if section = "header">
        ${e[2]}
    <#elseif section = "form">
        <p class="auth-card__text">${e[3]}</p>
        <#if e[4]?has_content>
            <div class="auth-card__actions"><@layout.primaryLink e[4] e[5]/></div>
        <#else>
            <@layout.link "Wróć do OpenKSeF" "/"/>
        </#if>
    </#if>
</@layout.registrationLayout>
