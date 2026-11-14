<#import "template.ftl" as layout>
<#assign emailError = layout.fieldError('email', 'username')>
<#assign emailExists = emailError?has_content && (emailError == msg("emailExistsMessage") || emailError == msg("usernameExistsMessage"))>
<@layout.registrationLayout displayMessage=messagesPerField.exists('global') displayInfo=true brandH1=true pageTitle="Rejestracja"; section>
    <#if section = "form">
        <p class="auth-card__desc">Utwórz nowe konto</p>
        <#if emailExists>
            <@layout.formError>Konto z tym adresem e-mail już istnieje. <a href="${url.loginUrl}">Zaloguj się</a> albo <a href="${url.loginResetCredentialsUrl}">ustaw nowe hasło</a>.</@layout.formError>
        </#if>
        <form id="kc-register-form" class="auth-card__form" action="${url.registrationAction}" method="post">
            <div class="auth-card__form-row">
                <@layout.field id="firstName" name="firstName" label="Imię" placeholder="Jan" value=layout.attr('firstName') autocomplete="given-name" error=layout.fieldError('firstName')/>
                <@layout.field id="lastName" name="lastName" label="Nazwisko" placeholder="Kowalski" value=layout.attr('lastName') autocomplete="family-name" error=layout.fieldError('lastName')/>
            </div>
            <@layout.field id="email" name="email" type="email" label="Email" placeholder="jan@firma.pl" value=layout.attr('email') autocomplete="email" error=emailExists?then('', emailError)/>
            <#if !realm.registrationEmailAsUsername>
                <input type="hidden" id="username" name="username" value="${layout.attr('username')}">
            </#if>
            <@layout.field id="password" name="password" type="password" label="Hasło" placeholder="Min. 8 znaków" autocomplete="new-password" error=layout.fieldError('password')/>
            <@layout.field id="password-confirm" name="password-confirm" type="password" label="Powtórz hasło" placeholder="Powtórz hasło" autocomplete="new-password" error=layout.fieldError('password-confirm')/>
            <#if termsAcceptanceRequired??>
                <label class="auth-check"><input type="checkbox" id="termsAccepted" name="termsAccepted" required> Akceptuję <a href="https://open-ksef.pl" target="_blank" rel="noopener">regulamin</a></label>
                <#if messagesPerField.existsError('termsAccepted')><span class="auth-field-error">${kcSanitize(messagesPerField.get('termsAccepted'))?no_esc}</span></#if>
            </#if>
            <@layout.primary text="Utwórz konto" pending="Tworzenie konta..."/>
        </form>
        <#if !realm.registrationEmailAsUsername>
            <script>document.getElementById('kc-register-form').addEventListener('submit', () => { document.getElementById('username').value = document.getElementById('email').value })</script>
        </#if>
    <#elseif section = "socialProviders">
        <@layout.google "Zarejestruj przez Google"/>
    <#elseif section = "info">
        <@layout.footer "Masz już konto?" "Zaloguj się" url.loginUrl/>
    </#if>
</@layout.registrationLayout>
