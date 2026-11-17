import type { ReactElement } from 'react'

import { useSetupStatus } from '@/hooks/useSetupStatus'
import { isEmailContact } from '@/privacy/dataController'

export function PrivacyPage(): ReactElement {
  const { data, isLoading } = useSetupStatus()
  const name = data?.dataControllerName
  const contact = data?.dataControllerContact

  return (
    <main className="auth-page">
      <article className="onboarding-card privacy-page">
        <h1 className="onboarding-title">Prywatność</h1>

        <section aria-labelledby="privacy-controller-title" data-testid="privacy-controller">
          <h2 id="privacy-controller-title">Administrator danych</h2>
          {isLoading ? (
            <p>Ładowanie…</p>
          ) : name ? (
            <>
              <p>
                Administratorem danych osobowych w tej instancji OpenKSeF jest <strong>{name}</strong>{name.endsWith('.') ? '' : '.'}
                {contact && (
                  <>
                    <br />
                    Kontakt: <strong>{isEmailContact(contact) ? <a href={`mailto:${contact}`}>{contact}</a> : contact}</strong>
                  </>
                )}
              </p>
              <p>
                W sprawach swoich danych, w tym dostępu, poprawienia lub usunięcia, kontaktuj się z administratorem. Możesz też złożyć skargę do Prezesa Urzędu Ochrony Danych Osobowych.
              </p>
              <p>Jeśli jesteś nabywcą i Twoje dane są na fakturze, w sprawie tych danych kontaktuj się ze sprzedawcą.</p>
            </>
          ) : (
            <p>
              Administrator tej instancji nie podał jeszcze swoich danych. Administratorem jest właściciel instancji, czyli osoba lub firma, która dała Ci dostęp. Zapytaj ją o nazwę i kontakt.
            </p>
          )}
        </section>

        <section aria-labelledby="privacy-data-title">
          <h2 id="privacy-data-title">Jakie dane przechowuje ta instancja</h2>
          <ul>
            <li><strong>Konto:</strong> adres e-mail, imię i nazwisko (jeśli podane), identyfikator użytkownika.</li>
            <li><strong>Firmy:</strong> NIP, nazwa, token lub certyfikat KSeF (zaszyfrowane), opcjonalny e-mail do powiadomień.</li>
            <li><strong>Dostęp do firm:</strong> firmy, do których masz dostęp, Twoja rola w każdej z nich i ustawienie powiadomień.</li>
            <li><strong>Faktury otrzymane z KSeF:</strong> nagłówek, pozycje, dane kontrahentów, numer rachunku sprzedawcy, dane faktora, oznaczenia płatności (MPP, zapłata) oraz status płatności z datą i osobą, która oznaczyła fakturę jako opłaconą.</li>
            <li><strong>Faktury sprzedaży z KSeF:</strong> numer, kwoty, daty, dane nabywcy i pozycje.</li>
            <li><strong>Faktury wystawione:</strong> dane sprzedawcy i nabywcy (także osoby prywatnej), nabywcy zagranicznego (kraj, numer VAT UE lub podatkowy), waluta i kurs, rachunek sprzedawcy, pozycje; po wysłaniu plik XML, numer KSeF, UPO lub powód odrzucenia.</li>
            <li><strong>Historia zmian w firmie:</strong> kto, kiedy i co zmienił, np. status płatności, token KSeF, zaproszenia, dostęp i role.</li>
            <li><strong>Zaproszenia:</strong> e-mail osoby zaproszonej, rola, kto zaprosił i kto przyjął, daty utworzenia, wygaśnięcia i przyjęcia.</li>
            <li><strong>Urządzenia:</strong> token urządzenia do powiadomień push.</li>
          </ul>
          <p>
            Dane są na serwerze właściciela tej instancji. OpenKSeF nie usuwa ich automatycznie. Zostają do czasu usunięcia firmy przez użytkownika albo usunięcia przez administratora.
          </p>
          <p>
            Dane mogą trafić do: KSeF (Ministerstwo Finansów), wykazu podatników VAT (przy <strong>Pobierz z NIP</strong>), usług powiadomień push (relay OpenKSeF, Google, Apple) oraz serwera poczty wybranego przez administratora, jeśli te funkcje są włączone.
          </p>
          <p>
            Pełna polityka prywatności oprogramowania OpenKSeF:{' '}
            <a href="https://open-ksef.pl/docs/polityka-prywatnosci" target="_blank" rel="noopener noreferrer">open-ksef.pl/docs/polityka-prywatnosci</a>
          </p>
        </section>
      </article>
    </main>
  )
}
