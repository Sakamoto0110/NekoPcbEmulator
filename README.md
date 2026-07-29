# NekoPcbEmulator

Emulador visual de PCBs com porta RX/TX emulada, feito pra servir de alvo de uma testing suite
externa. Cada placa "ligada" abre uma porta e uma janela mostrando a placa e seus periféricos.

- **PCB-A** — protocolo raw ASCII. 3 LEDs RGBA, LCD de caracteres 20x4 com 5 slots de memória,
  e um painel de LED RGBA de 360x120 com POINT / LINE / RECT.
- **PCB-B** — protocolo binário com framing. Grid 5x5 endereçável na ponta de um cabo flat,
  com cor e tempo de aceso por LED.

Solução .NET 10 / VS 2026 (`NekoPcbEmulator.slnx`).

---

## Rodando

```bash
dotnet run --project src/NekoPcbEmulator.App
```

O launcher abre com um card por placa. **POWER ON** abre a porta e a janela da placa; fechar a
janela desliga a placa e libera a porta.

Pra subir tudo já ligado (útil em CI ou script de teste):

```bash
dotnet run --project src/NekoPcbEmulator.App -- --power all
```

| Flag | Efeito |
|------|--------|
| `--power a` \| `b` \| `all` | liga as placas no startup |
| `--port-a <n>` / `--port-b <n>` | portas TCP (default 5001 / 5002) |
| `--pipe` | usa named pipe em vez de TCP |

Dentro de uma janela de placa, **F5** reseta aquela placa.

## Cliente de teste

Conecta na placa, roda uma sequência scriptada ou abre um prompt interativo. Serve tanto de
smoke test quanto de referência de como falar cada protocolo.

```bash
dotnet run --project src/NekoPcbEmulator.TestClient -- a --demo
dotnet run --project src/NekoPcbEmulator.TestClient -- b --demo
dotnet run --project src/NekoPcbEmulator.TestClient -- a           # prompt ASCII
dotnet run --project src/NekoPcbEmulator.TestClient -- b           # prompt binário
dotnet run --project src/NekoPcbEmulator.TestClient -- a --pipe    # via named pipe
```

Os dois demos exercitam de propósito os caminhos de erro (índice fora do range, comando
desconhecido, tamanho errado, CRC corrompido) — é o que você quer ver o RX da sua framework
lidando.

---

## A porta emulada

Ligar uma placa abre um servidor onde a sua testing suite conecta. Dois transportes,
selecionáveis por placa no launcher:

| Transporte | Endpoint | Quando usar |
|------------|----------|-------------|
| **TCP** (default) | `tcp://127.0.0.1:5001` (A), `:5002` (B) | qualquer linguagem conecta, fácil de scriptar |
| **Named pipe** | `\\.\pipe\pcb-a`, `\\.\pipe\pcb-b` | se parece muito mais com um device serial: stream de bytes puro, sem porta, sem handshake |

Ambos são stream de bytes sem estruturação — o framing é problema do protocolo, que é
exatamente o que um módulo RX/TX precisa exercitar. Vários clientes podem conectar ao mesmo
tempo; cada um tem seu próprio buffer de reassembly, então um cliente mandando lixo não
corrompe o stream do outro.

Não foi usada porta COM virtual de propósito: isso exigiria instalar um driver de kernel
(com0com) com privilégio de administrador. Se você precisar de uma COM real depois, dá pra
pontear um par com0com no named pipe sem tocar em nada aqui.

---

## Protocolos

- [`docs/protocol-a.md`](docs/protocol-a.md) — raw ASCII, statements terminados em `;`
- [`docs/protocol-b.md`](docs/protocol-b.md) — frames binários com CRC-16/CCITT

Resumo do que dá pra assumir nos dois:

- **Toda requisição gera exatamente uma resposta**, com correlação (o `SEQ` na B é ecoado).
  Sem isso o RX da sua framework nunca seria exercitado.
- Erros são reportados, não engolidos — cada um com um código estável.
- Os contadores de comandos, erros e ruído aparecem no silkscreen da placa, então dá pra
  verificar visualmente sem ler log.

---

## Estrutura

```
NekoPcbEmulator.slnx
├── src/NekoPcbEmulator.Core/            net10.0 — sem dependência de UI
│   ├── Rgba.cs                 cor RGBA 0xRRGGBBAA + composição
│   ├── LogSink.cs              buffer de log lock-free
│   ├── PcbHost.cs              liga dispositivo + porta ("ligar a placa")
│   ├── Transport/              TCP e named pipe sobre uma base comum
│   └── Devices/
│       ├── PcbA/               parser ASCII, LCD, painel de pixels
│       └── PcbB/               framing binário, CRC16, grid de LEDs
├── src/NekoPcbEmulator.App/             net10.0-windows — WinForms
│   ├── Forms/                  launcher, janela de placa, card
│   └── Rendering/              desenho das placas
└── src/NekoPcbEmulator.TestClient/      net10.0 — cliente de referência
```

O núcleo não conhece a UI. O estado do dispositivo é mutado pelas threads de socket sob lock e
lido pela thread de UI via snapshot; a janela consulta um `StateVersion` e só repinta quando
algo mudou de fato, então placa parada custa zero.

---

## Por que WinForms e não raylib

raylib é **single-window por processo** — não tem API de multi-window. Pra ter um launcher mais
N janelas de placa seria preciso um processo por janela mais IPC, o que é bastante complexidade
por nenhum ganho aqui.

O único ponto que parecia pesado, o painel de 360x120 (43.200 pixels), na prática não é: o
framebuffer sai do dispositivo já em BGRA premultiplicado, o que é bit a bit o que um bitmap
GDI+ `Format32bppPArgb` guarda. A atualização vira um memcpy por linha e o desenho vira um
único `DrawImage` com nearest-neighbor. Custo em microssegundos.

O resto do desenho é ainda mais barato porque a arte estática da placa (fibra de vidro, cobre,
encapsulamentos, silkscreen) é renderizada **uma vez** num layer cacheado — só LEDs, LCD e
painel são redesenhados por frame.

Alternativas consideradas, caso a necessidade mude:

| Opção | Multi-window | Observação |
|-------|--------------|------------|
| **WinForms + GDI+** | nativo | escolhido; suficiente com folga aqui |
| Silk.NET / OpenTK | sim (GLFW) | GPU de verdade; só compensa com milhares de sprites animados |
| Avalonia | sim | cross-platform, render via Skia; se um dia precisar rodar fora do Windows |
| WPF | sim | retained mode atrapalha mais do que ajuda pra desenho imediato |
| raylib | **não** | o bloqueador |

Todo desenho acontece num espaço de design fixo, escalado e centralizado na janela, então
resize e DPI funcionam sem nenhuma matemática espalhada pelo layout.
