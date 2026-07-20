# Utrzymanie i zgodność

## Wersje Jellyfin

Wtyczka korzysta z bibliotek serwera Jellyfin. Po aktualizacji serwera nie
zakładaj automatycznie zgodności. Najpierw:

1. zmień `JellyfinVersion` w `Directory.Build.props`,
2. zmień `targetAbi` w `build.yaml`,
3. zbuduj projekt bez ostrzeżeń,
4. uruchom wszystkie testy,
5. sprawdź konfigurację, wyszukiwanie i pobieranie na testowym Jellyfin,
6. dopiero wtedy zwiększ wersję i opublikuj tag.

## Stabilność API

Wtyczka pobiera grupy dynamicznie z AzureIllusion. Dodanie grupy lub nowych
napisów nie wymaga aktualizacji wtyczki. Nowe wydanie jest potrzebne dopiero po
zmianie kontraktu API albo mechanizmu Jellyfin.

## Wycofanie wadliwego wydania

1. Nie usuwaj poprzedniej sprawnej wersji z manifestu.
2. Napraw problem i wydaj wyższy numer wersji.
3. Jeśli paczka ujawniła sekret, usuń wydanie i natychmiast unieważnij sekret.
4. Nigdy nie zastępuj zawartości istniejącego tagu inną paczką bez zmiany wersji.
