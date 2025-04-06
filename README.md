# 💱 CurrencyExchangeAPI

### Opis projektu

Aplikacja konsolowa w języku **C# (.NET)** umożliwiająca pobieranie i przeglądanie **aktualnych kursów walut** za pomocą zewnętrznego API. Dane są przechowywane lokalnie w bazie **SQLite** z wykorzystaniem **Entity Framework Core**.

---

### Główne funkcje

- Pobieranie kursów z [open.er-api.com](https://open.er-api.com)
- Automatyczne zapisywanie i aktualizacja danych w bazie
- Sprawdzanie, czy dane są aktualne (ważne do 1h)
- Przegląd kursów dla popularnych walut (USD, EUR, GBP, JPY, CHF)
- Filtrowanie kursów według wartości progowej
- Wyświetlanie wszystkich walut i kursów w bazie
- Obsługa błędów API i bazy danych

---

### Technologie

- .NET / C#
- Entity Framework Core
- SQLite
- JSON API

---


### Przykładowy output

```
=== Kursy wymiany dla PLN ===
Ostatnia aktualizacja: 06.04.2025 12:45
----------------------------------------
USD  :     0.2523
EUR  :     0.2341
GBP  :     0.2015
...
```

---

### Autor

Projekt wykonany w celach edukacyjnych.  
Autor: Stefan Wojciechowski 

---
