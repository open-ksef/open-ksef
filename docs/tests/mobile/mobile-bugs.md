# Mobile App (OpenKSeF.Mobile) - Bug Report

Date: 2026-03-05
Branch: `hrefaktormobil`
Device: Android emulator (emulator-5554)

## Summary

| Bug | Severity | Status |
|-----|----------|--------|
| BUG-1: App crash on NIP-y tab (InvertedBoolConverter) | CRITICAL | FIXED |
| BUG-2: Missing Polish diacritics in XAML | MEDIUM | FIXED |
| BUG-3: Missing Polish diacritics in C# code | MEDIUM | FIXED |
| BUG-4: Error styling for "no NIP selected" state | LOW | FIXED |
| BUG-5: Deprecated `DisplayAlert` API (CS0618 warning) | LOW | FIXED |
| BUG-6: Deprecated `Frame` control used across all views | MEDIUM | FIXED |

---

## BUG-1: CRITICAL - App crashes when navigating to NIP-y tab

**Severity:** CRITICAL
**Steps to reproduce:**
1. Launch the app (already logged in)
2. Tap the "NIP-y" tab in bottom navigation

**Expected:** The NIP-y (Tenants) page opens showing the list of tenants.
**Actual:** The app crashes immediately (FATAL EXCEPTION).

**Error log (adb logcat):**
```
E AndroidRuntime: FATAL EXCEPTION: main
E AndroidRuntime: Process: com.openksef.mobile, PID: 6479
E AndroidRuntime: android.runtime.JavaProxyThrowable:
[Microsoft.Maui.Controls.Xaml.XamlParseException]: Position 49:29. StaticResource not found for key InvertedBoolConverter
    at OpenKSeF.Mobile.Views.TenantsPage.InitializeComponent
    at OpenKSeF.Mobile.Views.TenantsPage..ctor
```

**Root cause:** `TenantsPage.xaml` (line 49) references `{StaticResource InvertedBoolConverter}`, but this converter is not registered in `Resources/Styles/Styles.xaml`. Only `IsNotNullConverter` is defined there.

**Affected files:**
- `Views/TenantsPage.xaml` line 49 - `IsVisible="{Binding IsEmpty, Converter={StaticResource InvertedBoolConverter}}"`
- `Views/TenantFormPage.xaml` line 16 - `IsEnabled="{Binding IsEditMode, Converter={StaticResource InvertedBoolConverter}}"` (would also crash when opening form)

**Fix:** Add `<toolkit:InvertedBoolConverter x:Key="InvertedBoolConverter" />` to `Resources/Styles/Styles.xaml`.

---

## BUG-2: Missing Polish diacritics across all XAML pages

**Severity:** MEDIUM (UI/i18n)
**Description:** All Polish-language UI labels use ASCII-only text without diacritical marks (ą, ę, ś, ć, ź, ż, ó, ł, ń). This makes the text look unprofessional/broken to Polish users.

### LoginPage.xaml
| Line | Current text | Correct text |
|------|-------------|--------------|
| 17 | `Zaloguj sie, aby przegladac faktury z KSeF` | `Zaloguj się, aby przeglądać faktury z KSeF` |

### InvoiceListPage.xaml
| Line | Current text | Correct text |
|------|-------------|--------------|
| 30 | `Tryb offline — wyswietlane sa dane z pamieci podrecznej` | `Tryb offline — wyświetlane są dane z pamięci podręcznej` |
| 54 | `Nie znaleziono faktur dla tego NIP-u. Faktury pojawia sie po synchronizacji z KSeF.` | `Nie znaleziono faktur dla tego NIP-u. Faktury pojawią się po synchronizacji z KSeF.` |
| 58 | `Odswiez` | `Odśwież` |
| 137 | `Ponow` | `Ponów` |

### TenantsPage.xaml
| Line | Current text | Correct text |
|------|-------------|--------------|
| 32 | `Brak NIP-ow` | `Brak NIP-ów` |
| 36 | `Dodaj swoj pierwszy NIP, aby przegladac faktury z KSeF.` | `Dodaj swój pierwszy NIP, aby przeglądać faktury z KSeF.` |
| 60 | `Usun` | `Usuń` |

### TenantFormPage.xaml
| Line | Current text | Correct text |
|------|-------------|--------------|
| 22 | `Nazwa wyswietlana` | `Nazwa wyświetlana` |

### InvoiceDetailsPage.xaml
| Line | Current text | Correct text |
|------|-------------|--------------|
| 7 | `Szczegoly faktury` (Title) | `Szczegóły faktury` |
| 27 | `Ponow` | `Ponów` |
| 78 | `Data sprzedazy` | `Data sprzedaży` |
| 104 | `Pokaz kod QR` | `Pokaż kod QR` |

### QrCodePage.xaml
| Line | Current text | Correct text |
|------|-------------|--------------|
| 24 | `Ponow` | `Ponów` |
| 53 | `Zeskanuj kodem w aplikacji bankowej, aby wykonac przelew.` | `Zeskanuj kodem w aplikacji bankowej, aby wykonać przelew.` |
| 58 | `Udostepnij kod QR` | `Udostępnij kod QR` |

---

## BUG-3: Missing Polish diacritics in C# code (ViewModels/Services)

**Severity:** MEDIUM (UI/i18n)
**Description:** Same diacritic issue as BUG-2, but in C# string literals (error messages, alerts).

### ApiService.cs
| Line | Current text | Correct text |
|------|-------------|--------------|
| 125 | `Sesja wygasla. Zaloguj sie ponownie.` | `Sesja wygasła. Zaloguj się ponownie.` |

### LoginViewModel.cs
| Line | Current text | Correct text |
|------|-------------|--------------|
| 54 | `Logowanie nie powiodlo sie. Sprobuj ponownie.` | `Logowanie nie powiodło się. Spróbuj ponownie.` |

### InvoiceListViewModel.cs
| Line | Current text | Correct text |
|------|-------------|--------------|
| 94 | `Nie udalo sie zaladowac faktur:` | `Nie udało się załadować faktur:` |

### TenantsViewModel.cs
| Line | Current text | Correct text |
|------|-------------|--------------|
| 51 | `Nie udalo sie zaladowac NIP-ow:` | `Nie udało się załadować NIP-ów:` |

### TenantFormViewModel.cs
| Line | Current text | Correct text |
|------|-------------|--------------|
| 134 | `NIP musi skladac sie z 10 cyfr.` | `NIP musi składać się z 10 cyfr.` |

### QrCodeViewModel.cs
| Line | Current text | Correct text |
|------|-------------|--------------|
| 77 | `Nie udalo sie wygenerowac kodu QR:` | `Nie udało się wygenerować kodu QR:` |
| 104 | `Blad` / `Nie udalo sie udostepnic:` | `Błąd` / `Nie udało się udostępnić:` |

### InvoiceDetailsViewModel.cs
| Line | Current text | Correct text |
|------|-------------|--------------|
| 67 | `Nie udalo sie zaladowac faktury:` | `Nie udało się załadować faktury:` |

---

## BUG-4: Faktury page shows error when no NIP is selected

**Severity:** LOW (UX)
**Steps to reproduce:**
1. Launch the app (logged in, but no NIP selected)
2. Observe the Faktury tab

**Expected:** A friendly empty state or clear instruction to select a NIP.
**Actual:** An error-style message "Nie wybrano NIP-u. Wybierz NIP z listy." is shown in a red-bordered error frame at the bottom with a "Ponow" (Retry) button. The error frame style is misleading -- this is not an error, it's a normal app state that requires user action.

**Fix:** Separated "no NIP selected" into a dedicated `IsNoTenantSelected` state in `InvoiceListViewModel.cs` (no longer uses `ErrorMessage`). Added a friendly empty-state section in `InvoiceListPage.xaml` with an icon, explanatory text, and a "Przejdź do NIP-ów" button that navigates to the NIP-y tab via `Shell.Current.GoToAsync("//tenants")`.

---

## BUG-5: Deprecated `DisplayAlert` API (CS0618 warning)

**Severity:** LOW (code quality)
**Description:** Two ViewModels call `Shell.Current.DisplayAlert(...)` which is marked obsolete in .NET MAUI 10 (CS0618). Should be replaced with the current `Page.DisplayAlert` or another pattern.

**Affected files:**
- `ViewModels/QrCodeViewModel.cs` line 104 — `await Shell.Current.DisplayAlert("Błąd", ...)`
- `ViewModels/InvoiceDetailsViewModel.cs` line 83 — `await Shell.Current.DisplayAlert("Skopiowano", ...)`

---

## BUG-6: Deprecated `Frame` control used across all XAML views

**Severity:** MEDIUM (code quality / .NET 10 deprecation)
**Description:** All XAML views use the legacy `Frame` control which is deprecated in .NET MAUI 10. Should be replaced with `Border` which supports `StrokeShape` for rounded corners and `Stroke` for border color.

**Affected files:**
- `Views/InvoiceListPage.xaml` — 3 instances (offline indicator, invoice item card, error message)
- `Views/InvoiceDetailsPage.xaml` — 4 instances (amount card, vendor info, dates, KSeF identifiers)
- `Views/QrCodePage.xaml` — 1 instance (QR image container)
- `Views/TenantsPage.xaml` — 1 instance (tenant item card)

**Fix:** Replaced all `Frame` elements with `Border`:
- `CornerRadius="X"` → `StrokeShape="RoundRectangle X,X,X,X"`
- `BorderColor="Y"` → `Stroke="Y"`
- `HasShadow="False"` → removed (Border has no shadow by default)
- `Frame.GestureRecognizers` → `Border.GestureRecognizers`

---

## Plan naprawy

- [x] BUG-1: Dodanie InvertedBoolConverter do Styles.xaml
- [x] BUG-2: Polskie znaki diakrytyczne we wszystkich plikach XAML
- [x] BUG-3: Polskie znaki diakrytyczne w kodzie C# (ViewModels/Services)
- [x] BUG-4: Przyjazny stan „nie wybrano NIP" zamiast błędu na stronie Faktury
- [x] BUG-5: Zamiana deprecated `DisplayAlert` → `DisplayAlertAsync` (CS0618)
- [x] BUG-6: Zamiana deprecated `Frame` → `Border` we wszystkich widokach XAML
