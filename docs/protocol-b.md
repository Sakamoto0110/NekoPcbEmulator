# PCB-B — protocolo binário com framing

**Versão atual: `1`** (informada por `GET_INFO`), sem mudanças até o NekoPcbEmulator 1.2.1.

Montado só com peças padrão de firmware: sync word de 2 bytes, length explícito, sequence number,
opcode, payload e **CRC-16/CCITT-FALSE**. Nada proprietário — dá pra validar o checksum contra
qualquer implementação de prateleira.

Cada frame válido gera exatamente uma resposta, na ordem de chegada e só para o cliente que
perguntou. Não há mensagens não solicitadas. Várias respostas podem chegar numa leitura só.

## Frame

```
  0    1    2     3     4     5 ................ n    n+1  n+2
+----+----+-----+-----+-----+---------------------+----+----+
| A5 | 5A | LEN | SEQ | CMD |   DATA (LEN-2)      |  CRC16  |
+----+----+-----+-----+-----+---------------------+----+----+
```

| Campo | Tamanho | Descrição |
|-------|---------|-----------|
| SOF | 2 | `0xA5 0x5A` |
| LEN | 1 | tamanho do **corpo** (SEQ + CMD + DATA). Mínimo 2 |
| SEQ | 1 | sequence number, **ecoado na resposta** |
| CMD | 1 | opcode |
| DATA | LEN-2 | payload, até 253 bytes |
| CRC16 | 2 | big-endian |

- **Tamanho total = LEN + 5.**
- Todo campo multi-byte é **big-endian** (network order).
- **O CRC cobre o byte LEN mais o corpo** — ou seja, tudo entre o SOF e o próprio CRC. Um LEN
  corrompido também é pego.
- CRC-16/CCITT-FALSE (também chamado CRC-16/IBM-3740): polinômio `0x1021`, init `0xFFFF`,
  sem reflexão de entrada ou saída, sem XOR final. Valor de conferência: o CRC de `123456789` é
  `0x29B1`.

### Ressincronização

O decoder varre até achar `A5 5A`. Bytes antes disso são descartados como ruído e contabilizados
(aparecem como `NOISE` na janela da placa). Se o CRC falhar, o decoder avança **um único byte** e
volta a procurar — assim um falso sync dentro do lixo não engole um frame real logo em seguida.

Erros de enquadramento geram `NAK` com **`cmd = 0x00`**, porque não há opcode confiável:

| Erro | Quando | SEQ do `NAK` |
|------|--------|--------------|
| `BAD_CHECKSUM` | o CRC não bateu | o byte SEQ lido do frame, best-effort |
| `BAD_FRAME` | LEN menor que 2 | `0` |

Consequências que um cliente precisa prever:

- Ruído sem `A5 5A` não gera resposta nenhuma.
- A busca recomeça **dentro** do frame rejeitado. Se o payload contiver outro `A5 5A`, um único
  frame corrompido pode gerar mais de uma resposta, inclusive a execução de um frame válido
  embutido nele.
- Um LEN corrompido para um valor maior faz a placa esperar esses bytes, **sem timeout**, até
  260 por frame. As respostas das requisições seguintes só chegam depois que a ressincronização
  acontecer. Se uma resposta não vier, reconecte: cada conexão começa com o buffer vazio. Na
  serial, que não tem reconexão, envie 260 bytes `0x00` e descarte o que voltar.

---

## Comandos (host → placa)

Índice do LED: **`index = row * 5 + column`**, origem no canto superior esquerdo, 0..24.
Duração em milissegundos, `uint16` big-endian, **0 = permanece aceso indefinidamente**
(máximo 65535 ms).

| CMD | Nome | DATA | Bytes |
|-----|------|------|-------|
| `0x01` | `LED_SET` | `idx, r, g, b, a, dur_hi, dur_lo` | 7 |
| `0x02` | `LED_CLEAR` | `idx` | 1 |
| `0x03` | `CLEAR_ALL` | — | 0 |
| `0x04` | `SET_ALL` | `r, g, b, a, dur_hi, dur_lo` | 6 |
| `0x05` | `SET_MASK` | `mask[4], r, g, b, a, dur_hi, dur_lo` | 10 |
| `0x06` | `SET_BATCH` | `n, (idx, r, g, b, a, dur_hi, dur_lo) * n` | 1 + 7n |
| `0x10` | `PING` | — | 0 |
| `0x11` | `GET_STATE` | — | 0 |
| `0x12` | `GET_INFO` | — | 0 |

`SET_MASK` usa um `uint32` big-endian onde o bit *n* seleciona o LED *n*. LEDs fora da máscara
são **apagados**, então é um "set exato" e não um "ligue estes também". Bits acima do 24
são rejeitados com `BAD_PARAMETER`.

`SET_BATCH` valida todos os índices **antes** de aplicar qualquer um: ou o lote inteiro entra,
ou nada muda. `n` vai de 0 a 36 (o que cabe no payload); se um índice se repete, vale o último.

Todo `SET` substitui o LED por inteiro, inclusive uma contagem em andamento. Não há comando de
reset: `CLEAR_ALL` apaga os LEDs, mas não zera os contadores da janela.

## Respostas (placa → host)

Sempre com o SEQ da requisição ecoado.

| CMD | Nome | DATA |
|-----|------|------|
| `0x80` | `ACK` | `cmd` (o opcode aceito) |
| `0x81` | `NAK` | `cmd, error` |
| `0x90` | `PONG` | — |
| `0x91` | `STATE` | 25 × 7 bytes = 175 |
| `0x92` | `INFO` | `versão, rows, cols, nome ASCII…`, hoje `1, 5, 5, "PCB-B"` |

`STATE`, 7 bytes por LED em ordem de índice:

```
on(1)  r(1)  g(1)  b(1)  a(1)  remaining_ms(2, BE)
```

`remaining_ms` é 0 quando o LED está apagado ou aceso sem timeout.

Ao expirar, o LED volta ao estado de `LED_CLEAR`: apagado **e com a cor zerada**. A PCB-A usa a
mesma faixa de duração (0..65535 ms, 0 = indefinido), mas lá o LED expirado guarda a cor. Nas
duas placas a expiração não gera mensagem.

### Códigos de erro

| Código | Nome | Quando |
|--------|------|--------|
| `0x01` | `BAD_LENGTH` | DATA com tamanho errado pro comando |
| `0x02` | `UNKNOWN_COMMAND` | opcode não reconhecido |
| `0x03` | `BAD_INDEX` | índice de LED fora de 0..24 |
| `0x04` | `BAD_PARAMETER` | parâmetro inválido (ex.: bits altos na máscara) |
| `0x05` | `BAD_CHECKSUM` | CRC não bateu |
| `0x06` | `BAD_FRAME` | byte LEN impossível (menor que 2) |

---

## Exemplos

Acender o LED 12 (linha 2, coluna 2) em azul por 2000 ms, seq 7:

```
A5 5A 09 07 01 0C 20 60 FF FF 07 D0 9A DA
│  │  │  │  │  │  └──────┬─────┘ └───┬──┘
│  │  │  │  │  │         │           └── 0x07D0 = 2000 ms
│  │  │  │  │  │         └────────────── r=0x20 g=0x60 b=0xFF a=0xFF
│  │  │  │  │  └──────────────────────── idx 12
│  │  │  │  └─────────────────────────── CMD = LED_SET
│  │  │  └────────────────────────────── SEQ = 7
│  │  └───────────────────────────────── LEN = 9 (SEQ+CMD+7 de DATA)
└──┴──────────────────────────────────── SOF
```

Resposta:

```
A5 5A 03 07 80 01 91 35      // ACK de LED_SET, seq 7
```

`PING` e a resposta:

```
-> A5 5A 02 01 10 83 FC
<- A5 5A 02 01 90 12 74
```

Índice inválido:

```
-> A5 5A 09 03 01 63 FF FF FF FF 00 00 4F E8    // idx 0x63 = 99
<- A5 5A 04 03 81 01 03 0C EE                   // NAK de LED_SET, BAD_INDEX
```

`GET_INFO` e a resposta:

```
-> A5 5A 02 01 12 A3 BE
<- A5 5A 0A 01 92 01 05 05 50 43 42 2D 42 65 AA    // versão 1, 5x5, "PCB-B"
```

CRC errado (um `PING` com seq 9 e o último byte trocado):

```
-> A5 5A 02 09 10 0A 54
<- A5 5A 04 09 81 00 05 37 B2                   // NAK, cmd 0x00, BAD_CHECKSUM
```

---

## Implementação de referência

O encoder, o decoder e a tabela de CRC estão em
[`src/NekoPcbEmulator.Core/Devices/PcbB/`](../src/NekoPcbEmulator.Core/Devices/PcbB/) — o mesmo código roda nos
dois lados. O lado host está em
[`src/NekoPcbEmulator.TestClient/BinaryClient.cs`](../src/NekoPcbEmulator.TestClient/BinaryClient.cs), que é a
descrição mais curta do que uma implementação conforme precisa fazer.
