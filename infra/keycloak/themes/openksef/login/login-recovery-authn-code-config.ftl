<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false icon="key-round" iconKind="warning"; section>
    <#if section = "header">
        Zapisz kody zapasowe
    <#elseif section = "form">
        <p class="auth-card__text">Użyj ich, gdy nie masz telefonu z aplikacją. Każdy kod działa raz.</p>
        <@layout.notice "warning" "triangle-alert">Pokazujemy je tylko teraz. Zapisz je poza telefonem.</@layout.notice>
        <div class="auth-print">
            <p class="auth-print-email" hidden>${(auth.attemptedUsername)!''}</p>
            <ol class="auth-codes">
                <#list recoveryAuthnCodesConfigBean.generatedRecoveryAuthnCodesList as code>
                    <li>${code[0..3]}-${code[4..7]}-${code[8..]}</li>
                </#list>
            </ol>
        </div>
        <div class="auth-row-actions">
            <button type="button" class="ui-button ui-button--outline ui-button--md" data-action="copy-codes"><@layout.svg "copy" 14/> <span>Kopiuj</span></button>
            <button type="button" class="ui-button ui-button--outline ui-button--md" data-action="download-codes"><@layout.svg "download" 14/> <span>Pobierz</span></button>
            <button type="button" class="ui-button ui-button--outline ui-button--md" data-action="print-codes"><@layout.svg "printer" 14/> <span>Drukuj</span></button>
        </div>
        <form id="kc-recovery-codes-settings-form" class="auth-card__form" action="${url.loginAction}" method="post">
            <input type="hidden" name="generatedRecoveryAuthnCodes" value="${recoveryAuthnCodesConfigBean.generatedRecoveryAuthnCodesAsString}">
            <input type="hidden" name="generatedAt" value="${recoveryAuthnCodesConfigBean.generatedAt?c}">
            <input type="hidden" name="userLabel" value="Kody zapasowe">
            <label class="auth-check"><input type="checkbox" id="kcRecoveryCodesConfirmationCheck" data-enables="saveRecoveryAuthnCodesBtn" required> Zapisałem kody w bezpiecznym miejscu</label>
            <button type="submit" id="saveRecoveryAuthnCodesBtn" class="ui-button ui-button--primary auth-card__btn" disabled>Dalej</button>
        </form>
    </#if>
</@layout.registrationLayout>
