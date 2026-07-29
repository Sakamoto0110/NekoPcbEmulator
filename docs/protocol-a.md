# PCB-A — protocolo raw ASCII

Stream de bytes puro, sem framing binário. O host manda **statements** terminados por `;`,
a placa responde com uma linha `OK`/`ERR` também terminada por `;`.

- **Encoding:** Latin-1 (byte↔char 1:1, então ASCII puro funciona sem surpresa).
- **Terminador de comando:** `;`
- **Terminador de resposta:** `;\r\n`
- **Toda requisição gera exatamente uma resposta.** Isso é o que dá ao RX da sua framework
  algo pra receber e correlacionar.
- Whitespace antes de um statement é ignorado, então você pode mandar um comando por linha.
- Vários statements podem vir no mesmo pacote TCP: `LCD CLR;LCD SHOW;` é válido.
- Limite de 4096 bytes por statement. Passar disso devolve `ERR OVERFLOW` e descarta até o
  próximo `;`.

## Escapes

Uma barra invertida escapa o caractere seguinte, em qualquer posição. É isso que permite um
texto conter o próprio terminador:

| Escape | Resultado |
|--------|-----------|
| `\;`   | `;` literal (não termina o statement) |
| `\<` `\>` | `<` `>` literais |
| `\\`   | `\` |
| `\n` `\r` `\t` | LF, CR, TAB |

## Números

Onde a gramática pede um inteiro, são aceitos: decimal (`42`, `-7`), hex com prefixo
(`0xFF00A0FF`) e hex com cerquilha (`#FF00A0FF`).

**RGBA é um inteiro de 32 bits no layout `0xRRGGBBAA`.** O alfa é a intensidade emitida.

Booleanos aceitam `true/false`, `1/0`, `on/off`, `high/low`, `yes/no` (case-insensitive).

Separadores de argumento são vírgula **ou** espaço, e sequências deles colapsam. Então
`PANEL POINT<10, 20, 0xFF0000FF>` e `PANEL POINT<10 20 0xFF0000FF>` são equivalentes.

---

## LEDs indicadores

```
LIGHT[<IDX:int>] <RGBA:int> <STATE:bool>;
```

`IDX` vai de 0 a 2. A cor e o estado são independentes: o LED guarda a cor mesmo desligado,
então `LIGHT[0] 0xFF0000FF false;` seguido de `LIGHT[0] 0xFF0000FF true;` reacende no
vermelho.

```
LIGHT[0] 0xFF2020FF true;      -> OK;
LIGHT[9] 0xFFFFFFFF true;      -> ERR RANGE index 9 outside 0..2;
```

---

## LCD de caracteres (20x4, 5 slots)

Três buffers, exatamente na forma dos comandos:

| Buffer | Escrito por | Lido por |
|--------|-------------|----------|
| slots (5) | `LCD SAVE` | `LCD LOAD` |
| staging | `LCD LOAD`, `LCD TEXT` | `LCD SHOW` |
| tela | `LCD SHOW` | — |

```
LCD SAVE<<IDX:int>, <TXT:string>>;   // grava num slot (0..4)
LCD LOAD<<IDX:int>>;                 // slot -> staging
LCD TEXT<<TXT:string>>;              // texto inline -> staging
LCD SHOW;                            // staging -> tela
LCD CLR;                             // apaga a tela
```

Detalhes que importam pra testar:

- `LCD CLR` limpa **só a tela**. O staging e os slots continuam intactos, então
  `LCD CLR; LCD SHOW;` traz o mesmo texto de volta. Isso é proposital e testável.
- `LCD TEXT` **não** faz trim: espaços à esquerda são preservados, o que serve pra centralizar.
- `LCD SAVE` consome uma sequência de separadores depois do índice. Se você precisa de espaços
  à esquerda num slot, use um escape ou `LCD TEXT`.
- Layout: `\n` força quebra, linhas maiores que 20 colunas fazem wrap, e o que passar da 4ª
  linha é descartado — igual a um módulo real sem scroll.

```
LCD SAVE<0, BOOT OK>;                              -> OK;
LCD TEXT<linha 1\nlinha 2\ncom \; literal>;        -> OK;
LCD SHOW;                                          -> OK;
LCD LOAD<7>;                                       -> ERR RANGE slot 7 outside 0..4;
```

---

## Painel de pixels RGBA (360x120)

Origem no canto superior esquerdo. Tudo é recortado silenciosamente: desenhar fora do painel
não é erro.

```
PANEL POINT<<X>, <Y>, <RGBA>>;
PANEL LINE<<X0>, <Y0>, <X1>, <Y1>, <RGBA>>;        // Bresenham, extremos inclusos
PANEL RECT<<X>, <Y>, <W>, <H>, <RGBA>[, <FILL:bool>]>;
PANEL CLR;                                          // preenche com 0x00000000
PANEL CLR<<RGBA>>;                                  // preenche com uma cor
```

- `X,Y` do `RECT` é o canto superior esquerdo e o tamanho **inclui** ele. `W` ou `H` ≤ 0 não
  desenha nada e responde `OK`.
- `FILL` é opcional e default `false` (só contorno).
- **Alfa compõe source-over** contra o que já está no framebuffer, igual a uma API de desenho
  normal. Na tela o resultado é achatado contra preto, então o alfa lê como intensidade
  emitida — que é como um painel de LED se comporta.

```
PANEL CLR<0x0A1830FF>;                             -> OK;
PANEL RECT<10, 10, 100, 50, 0x00FF80FF, true>;     -> OK;
PANEL LINE<0, 0, 359, 119, 0xFFFFFF80>;            -> OK;
```

---

## Sistema

```
SYS PING;     -> OK PONG;
SYS ID;       -> OK PCB-A ASCII/1.0 LIGHTS=3 LCD=20x4x5 PANEL=360x120;
SYS STAT;     -> OK CMD=1204 ERR=3 LIT=8140;
SYS RESET;    -> OK;
```

---

## Respostas

```
OK;
OK <payload>;
ERR <CODE> <detalhe>;
```

| Código | Quando |
|--------|--------|
| `SYNTAX` | forma do comando errada (bracket faltando, lixo depois do comando) |
| `UNKNOWN_CMD` | verbo ou subcomando não reconhecido |
| `BAD_ARGS` | quantidade de argumentos errada, ou um argumento não parseia |
| `RANGE` | índice fora do intervalo válido |
| `OVERFLOW` | statement passou de 4096 bytes |
| `INTERNAL` | exceção inesperada no dispositivo (não deveria acontecer) |

O payload de uma resposta nunca contém `;`, `\r` ou `\n` — eles são substituídos antes do
envio, e o detalhe é truncado em 200 caracteres. Ou seja: você **sempre** pode fatiar a
resposta em `;` com segurança.
