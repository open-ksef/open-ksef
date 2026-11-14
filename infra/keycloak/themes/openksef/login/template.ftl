<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false brandH1=false icon="" iconKind="info" pageTitle="">
<#assign headerHtml><#nested "header"></#assign>
<#assign descHtml><#nested "desc"></#assign>
<#assign headerText = headerHtml?markup_string?replace("<[^>]*>", "", "r")?trim>
<!DOCTYPE html>
<html lang="pl">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="robots" content="noindex, nofollow">
    <title><#if pageTitle?has_content>${pageTitle} – OpenKSeF<#elseif headerText?has_content>${headerText} – OpenKSeF<#else>OpenKSeF</#if></title>
    <link rel="icon" type="image/svg+xml" href="${url.resourcesPath}/img/favicon.svg">
    <link rel="icon" type="image/png" sizes="32x32" href="${url.resourcesPath}/img/favicon-32.png">
    <link rel="apple-touch-icon" href="${url.resourcesPath}/img/favicon-180.png">
    <#list properties.styles?split(' ') as style>
    <link href="${url.resourcesPath}/${style}" rel="stylesheet">
    </#list>
    <#list properties.scripts?split(' ') as script>
    <script src="${url.resourcesPath}/${script}" defer></script>
    </#list>
</head>
<body class="${bodyClass}" data-page-id="login-${pageId}" data-icons="${url.resourcesPath}/img/icons.svg">
<main class="auth-page">
    <div class="auth-card">
        <div class="auth-logo"><@brandMark 40/></div>
        <#if brandH1><h1 class="auth-card__title">OpenKSeF</h1><#else><p class="auth-card__title">OpenKSeF</p></#if>
        <#if icon?has_content><div class="auth-card__icon auth-card__icon--${iconKind}"><@svg icon 24/></div></#if>
        <#if !brandH1 && headerText?has_content><h1 class="auth-card__heading">${headerHtml}</h1></#if>
        <#if descHtml?markup_string?trim?has_content><p class="auth-card__desc">${descHtml}</p></#if>
        <#if displayMessage && message?has_content && message.type != 'warning'>
            <#if message.type = 'error'>
                <@formError>${kcSanitize(message.summary)?no_esc}</@formError>
            <#else>
                <@notice message.type>${kcSanitize(message.summary)?no_esc}</@notice>
            </#if>
        </#if>
        <#nested "form">
        <#nested "socialProviders">
        <#if displayInfo><#nested "info"></#if>
    </div>
</main>
</body>
</html>
</#macro>

<#macro brandMark size><svg width="${size}" height="${size}" viewBox="0 0 32 32" aria-hidden="true"><path d="M20 2H10a8 8 0 0 0-8 8v12a8 8 0 0 0 8 8h12a8 8 0 0 0 8-8V12h-6v10a2 2 0 0 1-2 2H10a2 2 0 0 1-2-2V10a2 2 0 0 1 2-2h10zM22 2v8h8z" fill="currentColor"/></svg></#macro>

<#macro svg name size=16><svg class="ui-icon" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><use href="${url.resourcesPath}/img/icons.svg#i-${name}"/></svg></#macro>

<#macro formError><div class="ui-form-error auth-card__error" role="alert"><span class="ui-form-error__icon" aria-hidden="true">!</span><span><#nested></span></div></#macro>

<#macro notice kind icon=""><#assign i = icon?has_content?then(icon, {"success": "circle-check", "warning": "triangle-alert"}[kind]!"circle-alert")><div class="auth-notice auth-notice--${kind}" role="status"><@svg i/><span><#nested></span></div></#macro>

<#macro field id name label type="text" value="" placeholder="" error="" autocomplete="" attrs="" labelLink="" labelLinkText="" hint="" readonly=false required=true>
<div class="ui-form-group">
    <#if labelLink?has_content>
    <div class="auth-label-row"><label for="${id}">${label}</label><a href="${labelLink}">${labelLinkText}</a></div>
    <#else>
    <label for="${id}">${label}</label>
    </#if>
    <input id="${id}" name="${name}" type="${type}" value="${value}"<#if placeholder?has_content> placeholder="${placeholder}"</#if><#if autocomplete?has_content> autocomplete="${autocomplete}"</#if><#if required && !readonly> required</#if><#if readonly> readonly</#if><#if error?has_content> aria-invalid="true" aria-describedby="${id}-err"</#if> ${attrs?no_esc}>
    <#if error?has_content><span class="auth-field-error" id="${id}-err">${kcSanitize(error)?no_esc}</span></#if>
    <#if hint?has_content><span class="ui-form-hint">${hint}</span></#if>
</div>
</#macro>

<#macro codeField id name label placeholder error="" numeric=false>
    <#if numeric>
        <@field id=id name=name label=label placeholder=placeholder error=error autocomplete="one-time-code" attrs='class="auth-code-input" inputmode="numeric" maxlength="6" autofocus'/>
    <#else>
        <@field id=id name=name label=label placeholder=placeholder error=error autocomplete="off" attrs='class="auth-code-input" autocapitalize="characters" spellcheck="false" autofocus'/>
    </#if>
</#macro>

<#macro primary text pending="" name="" value=""><button type="submit" class="ui-button ui-button--primary auth-card__btn"<#if name?has_content> name="${name}"</#if><#if value?has_content> value="${value}"</#if><#if pending?has_content> data-pending="${pending}"</#if>>${text}</button></#macro>

<#macro secondary text name="" value="" href="">
    <#if href?has_content>
        <a class="ui-button ui-button--outline auth-card__btn" href="${href}">${text}</a>
    <#else>
        <button type="submit" class="ui-button ui-button--outline auth-card__btn" formnovalidate<#if name?has_content> name="${name}"</#if><#if value?has_content> value="${value}"</#if>>${text}</button>
    </#if>
</#macro>

<#macro primaryLink text href><a class="ui-button ui-button--primary auth-card__btn" href="${href}">${text}</a></#macro>

<#macro footer text link href><p class="auth-card__footer">${text} <a class="auth-card__link-btn" href="${href}">${link}</a></p></#macro>

<#macro link text href><a class="auth-card__link" href="${href}">${text}</a></#macro>

<#macro tryAnotherWay text>
    <#if auth?has_content && auth.showTryAnotherWayLink()>
        <form id="kc-select-try-another-way-form" action="${url.loginAction}" method="post" hidden>
            <input type="hidden" name="tryAnotherWay" value="on">
        </form>
        <a href="#" class="auth-card__link" id="try-another-way" onclick="document.getElementById('kc-select-try-another-way-form').requestSubmit(); return false">${text}</a>
    </#if>
</#macro>

<#macro google text>
    <#if social?? && social.providers?has_content>
        <#list social.providers?filter(p -> p.alias == "google") as p>
            <div class="auth-divider"><span>lub</span></div>
            <a class="auth-google-btn auth-card__btn" href="${p.loginUrl}"><svg class="auth-google-btn__icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 0 1-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1z" fill="#4285F4"/><path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" fill="#34A853"/><path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18A10.96 10.96 0 0 0 1 12c0 1.77.42 3.45 1.18 4.93l3.66-2.84z" fill="#FBBC05"/><path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" fill="#EA4335"/></svg>${text}</a>
        </#list>
    </#if>
</#macro>

<#function fieldError names...>
    <#list names as n>
        <#if messagesPerField.existsError(n)><#return messagesPerField.get(n)></#if>
    </#list>
    <#return "">
</#function>

<#function portalHome>
    <#return (pageRedirectUri?has_content)?then(pageRedirectUri!"", "/")>
</#function>

<#function attr name>
    <#return (profile.attributesByName[name].value)!"">
</#function>
