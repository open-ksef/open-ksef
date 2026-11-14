<#import "template.ftl" as layout>
<@layout.registrationLayout displayInfo=true; section>
    <#if section = "header">
        Jak chcesz się zweryfikować?
    <#elseif section = "form">
        <form id="kc-select-credential-form" class="auth-choice" action="${url.loginAction}" method="post">
            <#list auth.authenticationSelections as a>
                <#assign recovery = a.displayName?contains("recovery")>
                <#assign otp = a.displayName?contains("otp")>
                <button type="submit" name="authenticationExecution" value="${a.authExecId}">
                    <@layout.svg recovery?then("key-round", "smartphone") 20/>
                    <span class="auth-choice__main">
                        <b><#if otp>Kod z aplikacji<#elseif recovery>Kod zapasowy<#else>${msg(a.displayName)}</#if></b>
                        <span><#if otp>Google Authenticator, Microsoft Authenticator, Aegis<#elseif recovery>Jeden z kodów zapisanych przy włączaniu weryfikacji<#else>${msg(a.helpText)}</#if></span>
                    </span>
                    <@layout.svg "chevron-right"/>
                </button>
            </#list>
        </form>
    <#elseif section = "info">
        <@layout.footer "To nie Ty?" "Wróć do logowania" url.loginRestartFlowUrl/>
    </#if>
</@layout.registrationLayout>
