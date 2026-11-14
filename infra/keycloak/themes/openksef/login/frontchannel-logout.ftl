<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false pageTitle="Wylogowanie"; section>
    <#if section = "form">
        <div class="auth-spinner" role="status" aria-label="Wylogowywanie"></div>
        <h1 class="auth-card__heading">Wylogowujemy Cię…</h1>
        <#list logout.clients as c>
            <iframe src="${c.frontChannelLogoutUrl}" title="Wylogowanie" hidden></iframe>
        </#list>
        <#if logout.logoutRedirectUri?has_content>
            <script>
                <#outputformat "JavaScript">
                window.addEventListener('load', () => window.location.replace(${logout.logoutRedirectUri?c}))
                </#outputformat>
            </script>
            <noscript><@layout.link "Dalej" logout.logoutRedirectUri/></noscript>
        </#if>
    </#if>
</@layout.registrationLayout>
