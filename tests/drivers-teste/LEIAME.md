# Índice de teste

Um índice de drivers de mentira, para testar o app sem publicar nada no GitHub.
O "driver" é para o teclado Dell KB216 e pede uma interface que esse teclado não tem,
então o app mostra os controles, mas nunca chega a enviar nada para ele.

```
PERIALLS_DRIVERS=tests/drivers-teste dotnet run --project Perialls.csproj
```

Se mudar um arquivo daqui, atualize o sha256 dele em `index.json` (`sha256sum teste.json`).
