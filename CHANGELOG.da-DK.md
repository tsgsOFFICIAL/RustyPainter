# Ændringslog

Alle væsentlige ændringer i Rusty Painter er dokumenteret her.

## 0.1.1

### Opdateringer

- Testudgave, der skal vise, at en installeret 0.1.0 kan opdatere sig selv. Der er ingen andre ændringer.

## 0.1.0

### Appen

- Første build med sidebjælken, en startside og en side med indstillinger.

### Opdateringer

- Appen leder efter en nyere version på GitHub, når den starter, og viser, hvad der er ændret.
- Den kan selv hente og installere den nye version. Installerede kopier kører det nye installationsprogram, bærbare kopier bytter deres filer, og begge genstarter bagefter.
- Overførsler prøves igen ved en dårlig forbindelse og kontrolleres mod den kontrolsum, GitHub udgiver. Hvis en opdatering mislykkes, bliver din installerede version, som den var.
