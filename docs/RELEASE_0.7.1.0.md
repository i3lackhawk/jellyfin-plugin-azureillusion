# Polskie Napisy Anime 0.7.1.0 — kandydat do wdrożenia

Naprawiono pełne aktualizacje porcjami, niezależne etapy harmonogramu, rozróżnianie
awarii API, dopasowanie konkretnych elementów Jellyfin, zakresy numeracji odcinków,
kolejkowanie zapisu grup i potwierdzanie ręcznych pobrań po zapisaniu pliku.

Wtyczka uwzględnia, że jedna seria Jellyfin może zawierać kilka osobnych anime
AniList. Nie przypisuje identyfikatora rodzica wszystkim sezonom.

## Kontrola

- 80/80 testów pluginu; build Release bez ostrzeżeń i błędów.
- Paczka ZIP, MD5, SHA-256 i manifest sprawdzone lokalnie.
- 33/33 testy integracji strony związane z Jellyfin, API i zapisem ustawień.
- Kompilacja produkcyjna strony i TypeScript sprawdzone; po ostatniej poprawce
  obsługi zapytań POST wymagane końcowe sprawdzenie uruchomionej kompilacji.

## Kolejność wdrożenia

1. Najpierw wdrożyć WebSubs: zbiorczy endpoint `POST /api/public/v1/subtitles/revisions`,
   dokładne filtry identyfikatorów i wyjątek ochrony formularzy dla odczytu z kluczem API.
2. Sprawdzić autoryzowane zapytanie zbiorcze oraz identyfikację sezonów na rzeczywistych danych.
3. Opublikować to wydanie i stabilny katalog. Zainstalować je w Jellyfin.
4. Przy braku aktywnego odtwarzania zrestartować Jellyfin i sprawdzić stan Active,
   wersję 0.7.1.0, połączenie, zapis grup i napisy sezonów 2 oraz 3 Shield Hero.

Szkic nie jest potwierdzeniem wdrożenia na serwerze. Starsza strona obsługuje
wolniejszy tryb zgodności, ale dla dużej biblioteki aktualizację strony należy
wykonać przed uruchomieniem pełnej konserwacji.
