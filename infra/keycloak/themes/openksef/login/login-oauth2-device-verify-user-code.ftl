<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false icon="smartphone"; section>
    <#if section = "header">
        Logowanie telefonu
    <#elseif section = "desc">
        Wpisz kod widoczny w aplikacji OpenKSeF na Twoim telefonie.
    <#elseif section = "form">
        <#if message?has_content && message.type == "error">
            <@layout.formError>${kcSanitize(message.summary)?no_esc}</@layout.formError>
        </#if>
        <form id="kc-user-verify-device-user-code-form" class="auth-card__form" action="${url.oauth2DeviceVerificationAction}" method="post">
            <@layout.codeField id="device-user-code" name="device_user_code" label="Kod z telefonu" placeholder="ABCD-EFGH"/>
            <@layout.primary text="Dalej" pending="Sprawdzanie..."/>
        </form>
        <@layout.notice "warning" "triangle-alert">Wpisz tylko kod z własnego telefonu. Nikt z OpenKSeF nie poprosi Cię o ten kod.</@layout.notice>
        <@layout.link "Wróć do OpenKSeF" "/"/>
    </#if>
</@layout.registrationLayout>
