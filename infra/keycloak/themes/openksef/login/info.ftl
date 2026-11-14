<#import "template.ftl" as layout>
<#assign s = (message.summary)!"">
<#assign header = messageHeader!"">
<#assign home = layout.portalHome()>
<#if header == "oauth2DeviceVerificationCompleteHeader" || s == msg("oauth2DeviceVerificationCompleteMessage")>
    <#assign i = ["success", "circle-check", "Telefon zalogowany", "Wróć do telefonu. Aplikacja otworzy się sama. Tę kartę możesz zamknąć.", "", ""]>
<#elseif header == "oauth2DeviceVerificationFailedHeader" || s == msg("oauth2DeviceVerificationFailedMessage") || s == msg("oauth2DeviceConsentDeniedMessage")>
    <#assign i = ["error", "circle-x", "Nie zalogowano telefonu", "Odrzuciłeś logowanie. Jeśli to pomyłka, poproś w aplikacji o nowy kod.", "", ""]>
<#elseif s == msg("emailVerifiedMessage") || s == msg("emailVerifiedAlreadyMessage")>
    <#assign i = ["success", "circle-check", "Adres email potwierdzony", "Konto jest aktywne. Możesz zacząć pracę.", "Przejdź do OpenKSeF", home]>
<#elseif s == msg("accountPasswordUpdatedMessage") || s == msg("accountUpdatedMessage")>
    <#assign i = ["success", "circle-check", "Hasło zmienione", "Następnym razem zaloguj się nowym hasłem.", "Przejdź do OpenKSeF", home]>
<#elseif actionUri?has_content && !requiredActions??>
    <#assign i = ["info", "mail", "Potwierdź adres email", "", "Potwierdź adres", actionUri]>
<#elseif requiredActions??>
    <#assign i = ["info", "circle-alert", "Jeszcze jeden krok", "", "Dalej", actionUri!home]>
<#else>
    <#assign i = ["info", "circle-alert", "Informacja", s, "", ""]>
</#if>
<@layout.registrationLayout displayMessage=false icon=i[1] iconKind=i[0]; section>
    <#if section = "header">
        ${i[2]}
    <#elseif section = "form">
        <#if i[2] == "Potwierdź adres email">
            <#if (user.email)??>
                <p class="auth-card__text">Kliknij przycisk, aby potwierdzić adres <strong>${user.email}</strong>.</p>
            <#else>
                <p class="auth-card__text">${kcSanitize(s)?no_esc}</p>
            </#if>
        <#elseif i[2] == "Jeszcze jeden krok">
            <p class="auth-card__text">Aby aktywować konto, <#list requiredActions as a>${msg("requiredAction.${a}")}<#sep>, </#list>.</p>
        <#else>
            <p class="auth-card__text">${kcSanitize(i[3])?no_esc}</p>
        </#if>
        <#if i[4]?has_content>
            <div class="auth-card__actions"><@layout.primaryLink i[4] i[5]/></div>
        </#if>
        <#if !i[4]?has_content || i[2] == "Jeszcze jeden krok">
            <@layout.link "Wróć do OpenKSeF" home/>
        </#if>
    </#if>
</@layout.registrationLayout>
