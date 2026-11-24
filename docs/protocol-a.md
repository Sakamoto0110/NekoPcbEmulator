# PCB-A — protocolo raw ASCII

**Versão atual: `ASCII/1.1`** (informada por `SYS ID`, disponível desde o NekoPcbEmulator
1.2.0). O que mudou em relação à 1.0 está em [Histórico de versões](#histórico-de-versões).

Stream de bytes puro, sem framing binário. O host manda **statements** terminados por `;`,
a placa responde com uma linha `OK`/`ERR` também terminada por `;`.

- **Encoding:** Latin-1 (byte↔char 1:1, então ASCII puro funciona sem surpresa).
- **Terminador de comando:** `;`
- **Terminador de resposta:** `;\r\n`
- **Todo statement não vazio gera exatamente uma resposta, na ordem de chegada.** Não há ID de
  correlação: a ordem é a correlação. A resposta vai só para o cliente que perguntou.
- **Statement vazio é ignorado sem resposta.** `;`, `;;` e `  ;` não respondem nem contam.
- Whitespace em volta de um statement é ignorado, então você pode mandar um comando por linha.
- Vários statements podem vir no mesmo pacote TCP: `LCD CLR;LCD SHOW;` é válido. As respostas
  podem chegar juntas numa leitura só: fatie por `;`.
- Um statement sem `;` espera o resto indefinidamente, e é descartado se o cliente desconectar.
- Comandos são **case-insensitive**, e espaço antes de `[` ou `<` é aceito.
- Limite de 4096 bytes por statement, sem contar o `;` nem o whitespace inicial; escapes contam
  os dois bytes. Passar disso devolve um único `ERR OVERFLOW` e descarta até o próximo `;` não
  escapado.

## Escapes

Uma barra invertida escapa o caractere seguinte, em qualquer posição. É isso que permite um
texto conter o próprio terminador:

| Escape | Resultado |
|--------|-----------|
| `\;`   | `;` literal (não termina o statement) |
| `\<` `\>` | `<` `>` literais |
| `\\`   | `\` |
| `\n` `\r` `\t` `\0` | LF, CR, TAB, NUL |
| `\` + outro caractere | o próprio caractere |

Os escapes são traduzidos nos textos do LCD. Um par `<...>` balanceado dentro de um texto também
é aceito sem escape (`LCD TEXT<a<b>c>` vale `a<b>c`); um `>` solto precisa de `\>`.

## Números

Onde a gramática pede um inteiro, são aceitos: decimal (`42`, `-7`), hex com prefixo
(`0xFF00A0FF`) e hex com cerquilha (`#FF00A0FF`).

Índices, coordenadas e durações são inteiros de 32 bits com sinal. Um número que nem cabe nisso
é `BAD_ARGS`, não `RANGE`: `LIGHT[99]` é `RANGE`, `LIGHT[99999999999]` é `BAD_ARGS`.

**RGBA é um inteiro de 32 bits no layout `0xRRGGBBAA`.** O alfa é a intensidade emitida. Negativo
ou acima de `0xFFFFFFFF` é `BAD_ARGS`.

Booleanos aceitam `true/false`, `1/0`, `on/off`, `high/low`, `yes/no` (case-insensitive).

Separadores de argumento são vírgula **ou** espaço, e sequências deles colapsam. Então
`PANEL POINT<10, 20, 0xFF0000FF>` e `PANEL POINT<10 20 0xFF0000FF>` são equivalentes.

---

## LEDs indicadores

```
LIGHT[<IDX:int>] <RGBA:int> <STATE:bool>[ <MS:int>];
```

`IDX` vai de 0 a 15, num grid 4x4: **`index = row * 4 + column`**, origem no canto superior
esquerdo.

```
 0   1   2   3
 4   5   6   7
 8   9  10  11
12  13  14  15
```

| Argumento | Obrigatório | Valores | Significado |
|-----------|-------------|---------|-------------|
| `IDX` | sim | 0..15 | posição no grid |
| `RGBA` | sim | `0xRRGGBBAA` | cor; o alfa é o brilho |
| `STATE` | sim | booleano | liga ou desliga |
| `MS` | não (default `0`) | 0..65535 | tempo até apagar sozinho; `0` = sem tempo |

Cada comando endereça um único LED, e os LEDs são independentes entre si.

A cor e o estado também são independentes: o LED guarda a cor mesmo desligado,
então `LIGHT[0] 0xFF0000FF false;` seguido de `LIGHT[0] 0xFF0000FF true;` reacende no
vermelho.

### Auto-desligamento

`MS` é o tempo em milissegundos até o LED apagar sozinho, na mesma faixa da duração da PCB-B.
Omitir ou mandar `0` deixa aceso indefinidamente.

**Contagem**

- Começa quando a placa aceita o comando, não quando o cliente o envia.
- É por LED e pertence à placa: continua mesmo que o cliente que a iniciou desconecte.
- A precisão é da ordem de 16 ms, que é a granularidade do relógio do sistema. Um teste deve
  dar folga em vez de esperar o instante exato.

**Expiração**

- Equivale a um `STATE false`: o LED apaga e **a cor continua guardada**. Isso difere da
  PCB-B, que zera a cor ao expirar.
- **Não gera mensagem.** Continua valendo exatamente uma resposta por requisição.
- O estado dos LEDs não é legível pelo protocolo: o `LIT` de `SYS STAT` conta pixels do painel,
  não LEDs. Na janela, o anel em volta do LED mostra o tempo restante.

**Substituição e cancelamento**

| Com uma contagem em andamento, enviar... | Resultado |
|------------------------------------------|-----------|
| `LIGHT[i] <cor> true <MS>` com `MS` > 0 | reinicia a contagem com o novo tempo |
| `LIGHT[i] <cor> true` (ou `MS` = 0) | cancela a contagem; fica aceso |
| `LIGHT[i] <cor> false` (ou `MS` = 0) | cancela a contagem; apaga agora |
| um `LIGHT` que a placa recusa | nada muda: cor, estado e contagem seguem como estavam |
| `SYS RESET` | cancela tudo, apaga e esquece a cor de todos os LEDs |

**Validação**, nesta ordem; a primeira falha define a resposta:

1. há um bloco `[...]` (`SYNTAX`)
2. `IDX` é um inteiro (`BAD_ARGS`) e está em 0..15 (`RANGE`)
3. há 2 ou 3 argumentos (`BAD_ARGS`)
4. `RGBA` e depois `STATE` são válidos (`BAD_ARGS`)
5. `MS`, se presente, é um inteiro (`BAD_ARGS`) e está em 0..65535 (`RANGE`)
6. `MS` maior que 0 vem com `STATE true` (`BAD_ARGS`): tempo para um LED apagado é erro de
   quem chamou, então é reportado em vez de ignorado

```
LIGHT[0] 0xFF2020FF true;          -> OK;      aceso até segunda ordem
LIGHT[3] 0x20FF60FF true 2000;     -> OK;      apaga sozinho em 2 s
LIGHT[3], 0x20FF60FF, true, 2000;  -> OK;      vírgulas também servem
LIGHT[3] 0x20FF60FF true 0;        -> OK;      0 = sem tempo
LIGHT[3] 0x20FF60FF false 0;       -> OK;      desliga; 0 é aceito com false
LIGHT[16] 0xFFFFFFFF true;         -> ERR RANGE index 16 outside 0..15;
LIGHT[3] 0x20FF60FF true 70000;    -> ERR RANGE duration 70000 outside 0..65535;
LIGHT[3] 0x20FF60FF true soon;     -> ERR BAD_ARGS bad duration 'soon';
LIGHT[3] 0x20FF60FF false 2000;    -> ERR BAD_ARGS a duration needs state true;
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
- `LCD SAVE` consome uma sequência de separadores depois do índice e corta espaços no fim. Se
  você precisa de espaços à esquerda num slot, escape o primeiro (`LCD SAVE<0, \  texto>`).
- `LCD SAVE<0>` sem texto esvazia o slot.
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
- `PANEL CLR` **substitui** os pixels; só `POINT`, `LINE` e `RECT` compõem.
- **Alfa compõe source-over** contra o que já está no framebuffer, igual a uma API de desenho
  normal. Na tela o resultado é achatado contra preto, então o alfa lê como intensidade
  emitida — que é como um painel de LED se comporta.
- O contorno do `RECT` é feito de quatro linhas que compartilham os cantos, então com alfa
  parcial os cantos são compostos mais de uma vez.

```
PANEL CLR<0x0A1830FF>;                             -> OK;
PANEL RECT<10, 10, 100, 50, 0x00FF80FF, true>;     -> OK;
PANEL LINE<0, 0, 359, 119, 0xFFFFFF80>;            -> OK;
```

---

## Sistema

```
SYS PING;     -> OK PONG;
SYS ID;       -> OK PCB-A ASCII/1.1 LIGHTS=16 LCD=20x4x5 PANEL=360x120;
SYS STAT;     -> OK CMD=1204 ERR=3 LIT=8140;
SYS RESET;    -> OK;
```

- `SYS ID` descreve a placa: `ASCII/<versão do protocolo>`, `LIGHTS=<quantidade>`,
  `LCD=<colunas>x<linhas>x<slots>` e `PANEL=<largura>x<altura>`. É por ele que um cliente
  descobre quantos LEDs existem e se `MS` é suportado.
- `SYS STAT` devolve os contadores da placa, somando todos os clientes:
  - `CMD`: statements não vazios recebidos, aceitos ou recusados, incluindo o próprio `SYS STAT`.
    Um `OVERFLOW` não entra aqui.
  - `ERR`: respostas `ERR`, incluindo `OVERFLOW`.
  - `LIT`: pixels do painel com **alfa diferente de 0**. Preto opaco (`0x000000FF`) conta.
- `SYS RESET` limpa LEDs e contagens de tempo, LCD (tela, staging e slots), painel e contadores.
  Como o próprio reset zera `CMD`, um `SYS STAT` logo em seguida responde `CMD=1 ERR=0 LIT=0`.
  A tecla F5 na janela da placa faz o mesmo.

---

## Respostas

```
OK;
OK <payload>;
ERR <CODE> <detalhe>;
```

O **código** é o contrato; o detalhe é texto informativo e pode mudar entre versões.

| Código | Quando |
|--------|--------|
| `SYNTAX` | forma do comando errada (bracket faltando, lixo depois do comando) |
| `UNKNOWN_CMD` | verbo ou subcomando não reconhecido |
| `BAD_ARGS` | quantidade de argumentos errada, um argumento não parseia, ou `MS` > 0 com `STATE false` |
| `RANGE` | índice, slot ou duração fora do intervalo válido |
| `OVERFLOW` | statement passou de 4096 bytes |
| `INTERNAL` | exceção inesperada no dispositivo (não deveria acontecer) |

O payload de uma resposta nunca contém `;`, `\r` ou `\n` — eles são substituídos antes do
envio, e o detalhe é truncado em 200 caracteres. Ou seja: você **sempre** pode fatiar a
resposta em `;` com segurança.

---

## Histórico de versões

### ASCII/1.1 — NekoPcbEmulator 1.2.0

- Indicadores passam de 3 para **16**, num grid 4x4.
- `LIGHT` ganha o argumento opcional `MS` (auto-desligamento).

Comandos `LIGHT` de dois argumentos continuam funcionando sem mudança. O que um cliente da 1.0
pode precisar ajustar:

| | ASCII/1.0 | ASCII/1.1 |
|---|---|---|
| `SYS ID` | `ASCII/1.0 LIGHTS=3` | `ASCII/1.1 LIGHTS=16` |
| `LIGHT[3]` a `LIGHT[15]` | `ERR RANGE` | `OK` |
| detalhe do erro de índice | `outside 0..2` | `outside 0..15` |
| terceiro argumento do `LIGHT` | `ERR BAD_ARGS` | `MS` |

### ASCII/1.0 — até o NekoPcbEmulator 1.1.0

Versão inicial: 3 indicadores, sem tempo.
