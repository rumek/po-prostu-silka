---
change_id: member-entity-and-accountless-members
title: Member as a first-class entity, independent of the Identity account
status: planned
created: 2026-09-07
updated: 2026-09-07
---

## Notes

Admin musi móc prowadzić kartotekę osoby, która nie ma konta w systemie — zapisać ją na zajęcia,
przypisać plan. Gdy taka osoba zechce konto, zakłada je z kodem od admina i wchodzi na swoją
istniejącą historię zamiast zaczynać od zera.

Skutek techniczny: `AspNetUsers` przestaje być "osobą w klubie" i zostaje samym loginem. Nowa encja
`Member` przejmuje FK z rezerwacji, planów i grafiku; `Member.UserId` (nullable) jest łącznikiem.
