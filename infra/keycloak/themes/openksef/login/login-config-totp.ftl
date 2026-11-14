<#import "template.ftl" as layout>
<#assign aia = isAppInitiatedAction??>
<#assign account = (auth.attemptedUsername)!"">
<#assign otpauth = "otpauth://totp/OpenKSeF:" + account?url('UTF-8') + "?secret=" + totp.totpSecretEncoded?replace(" ", "") + "&issuer=OpenKSeF&digits=" + totp.policy.digits?c + "&period=" + totp.policy.period?c + "&algorithm=" + totp.policy.algorithm?remove_beginning("Hmac")>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('totp','userLabel'); section>
    <#if section = "header">
        <#if aia>Zmień aplikację uwierzytelniającą<#else>Włącz weryfikację dwuetapową</#if>
    <#elseif section = "desc">
        <#if aia>Stara aplikacja przestanie działać po zapisaniu.<#else>To zajmie około minuty. Bez tego nie wejdziesz do OpenKSeF.</#if>
    <#elseif section = "form">
        <#if messagesPerField.existsError('totp')>
            <@layout.formError>Nieprawidłowy kod. Wpisz kod, który aplikacja pokazuje teraz.</@layout.formError>
        </#if>
        <ol class="auth-steps">
            <li>Zainstaluj na telefonie aplikację uwierzytelniającą, np. Google Authenticator, Microsoft Authenticator albo Aegis.</li>
            <li>Zeskanuj kod QR w aplikacji.
                <div class="auth-steps__body">
                    <div class="auth-qr"><img src="data:image/png;base64, ${totp.totpSecretQrCode}" alt="Kod QR do aplikacji uwierzytelniającej"></div>
                    <span class="settings-muted">Nie możesz zeskanować? Wpisz klucz ręcznie:</span>
                    <div class="auth-secret"><span>${totp.totpSecretEncoded}</span><button type="button" class="ui-button ui-button--outline ui-button--sm" data-action="copy-secret" aria-label="Kopiuj klucz"><@layout.svg "copy" 14/> <span>Kopiuj</span></button></div>
                    <a href="${otpauth}" class="auth-card__link" style="margin:0">Otwórz w aplikacji na tym telefonie</a>
                </div>
            </li>
            <li>Wpisz 6-cyfrowy kod, który pokazuje aplikacja.
                <div class="auth-steps__body">
                    <form id="kc-totp-settings-form" class="auth-card__form" action="${url.loginAction}" method="post">
                        <@layout.codeField id="totp" name="totp" label="Kod z aplikacji" placeholder="000000" numeric=true/>
                        <input type="hidden" name="totpSecret" value="${totp.totpSecret}">
                        <input type="hidden" name="userLabel" value="Telefon">
                        <@layout.primary text=aia?then("Zapisz", "Włącz weryfikację") pending="Zapisywanie..."/>
                        <#if aia><@layout.secondary text="Anuluj" name="cancel-aia" value="true"/></#if>
                    </form>
                </div>
            </li>
        </ol>
    </#if>
</@layout.registrationLayout>
