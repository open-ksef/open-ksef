<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=messagesPerField.exists('global'); section>
    <#if section = "header">
        Uzupełnij dane konta
    <#elseif section = "desc">
        Pierwsze logowanie przez Google
    <#elseif section = "form">
        <form id="kc-idp-review-profile-form" class="auth-card__form" action="${url.loginAction}" method="post">
            <div class="auth-card__form-row">
                <@layout.field id="firstName" name="firstName" label="Imię" placeholder="Jan" value=layout.attr('firstName') autocomplete="given-name" error=layout.fieldError('firstName')/>
                <@layout.field id="lastName" name="lastName" label="Nazwisko" placeholder="Kowalski" value=layout.attr('lastName') autocomplete="family-name" error=layout.fieldError('lastName')/>
            </div>
            <@layout.field id="email" name="email" type="email" label="Email" value=layout.attr('email') readonly=layout.attr('email')?has_content hint="Adres z konta Google." error=layout.fieldError('email', 'username')/>
            <#if !realm.registrationEmailAsUsername && layout.attr('username')?has_content>
                <input type="hidden" name="username" value="${layout.attr('username')}">
            </#if>
            <@layout.primary text="Dalej" pending="Zapisywanie..."/>
        </form>
    </#if>
</@layout.registrationLayout>
