<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false icon="user"; section>
    <#if section = "header">
        Masz już konto OpenKSeF
    <#elseif section = "form">
        <p class="auth-card__text">Konto z adresem <strong>${(brokerContext.email)!((brokerContext.username)!'')}</strong> już istnieje. Połącz je z Google, aby logować się jednym kliknięciem.</p>
        <form class="auth-card__actions" action="${url.loginAction}" method="post">
            <@layout.primary text="Połącz z kontem Google" name="submitAction" value="linkAccount"/>
            <@layout.secondary text="Anuluj" href=url.loginRestartFlowUrl/>
        </form>
    </#if>
</@layout.registrationLayout>
