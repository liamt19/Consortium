# Consortium
Like MultiPV, but for engines instead of lines.

Starts up multiple UCI engines, sends each of them the same commands and aggregates their responses.

---

## Set up config.json:

**"sync_by_depth"**
* For "go" commands, wait for all engines to reach depth X before printing any output for that depth.
  - If false, print engine outputs as soon as they arrive.

**"default_opts"**
* UCI options to set for all engines unless overridden by engine-specific options

### Per-engine settings:
**"opts"**
* UCI options sent verbatim after initial uci command
* Specify the command in full, e.g. "setoption name x value y"

**"remapped_cmds"**
* Remap some input command to a different command, using the format "original;remapped"
* For example: "eval;raweval" means send that specific engine "raweval" when you type "eval"

---

## Example usage:
```
position fen 8/1bbb4/1bkb4/1bbb4/4NNN1/4NKN1/4NNN1/8 w - - 0 1
     horsie-1.1.6 << position fen 8/1bbb4/1bkb4/1bbb4/4NNN1/4NKN1/4NNN1/8 w - - 0 1
viridithas-18.0.0 << position fen 8/1bbb4/1bkb4/1bbb4/4NNN1/4NKN1/4NNN1/8 w - - 0 1
stormphrax-7.0.40 << position fen 8/1bbb4/1bkb4/1bbb4/4NNN1/4NKN1/4NNN1/8 w - - 0 1
 pawnocchio-1.8.1 << position fen 8/1bbb4/1bkb4/1bbb4/4NNN1/4NKN1/4NNN1/8 w - - 0 1

eval
     horsie-1.1.6 << eval
viridithas-18.0.0 << raweval
stormphrax-7.0.40 << raweval
 pawnocchio-1.8.1 << nneval
     horsie-1.1.6 >> -274
viridithas-18.0.0 >> -395
stormphrax-7.0.40 >> -465
 pawnocchio-1.8.1 >> raw eval: -2182
 pawnocchio-1.8.1 >> scaled eval: -2139
 pawnocchio-1.8.1 >> scaled and normalized eval: -1001

go depth 3
     horsie-1.1.6 << go depth 3
viridithas-18.0.0 << go depth 3
stormphrax-7.0.40 << go depth 3
 pawnocchio-1.8.1 << go depth 3
     horsie-1.1.6 >> D:   1/13   S:     cp 34  N:         420  T:        1  M: e3d5 b5e2 g3e2 c5f2 g4f2 b6f2 e4f2
viridithas-18.0.0 >> D:   1/13   S:     cp 13  N:         981  T:        1  M: f4d5 d7g4 f2g4 c5e3
stormphrax-7.0.40 >> D:   1/6    S:     cp 31  N:          28  T:        0  M: e3d5
 pawnocchio-1.8.1 >> D:   1/2    S:   cp -322  N:          67  T:        1  M: f4d5

     horsie-1.1.6 >> D:   2/8    S:     cp 34  N:         478  T:        1  M: e3d5 b5e2 g3e2 c5f2 g4f2 b6f2
viridithas-18.0.0 >> D:   2/6    S:     cp 13  N:        1044  T:        1  M: f4d5 d7g4 f2g4
stormphrax-7.0.40 >> D:   2/5    S:     cp 31  N:          77  T:        0  M: e3d5 b5e2
 pawnocchio-1.8.1 >> D:   2/3    S:   cp -371  N:         272  T:        1  M: f4d5 d7e6

     horsie-1.1.6 >> D:   3/9    S:     cp 34  N:         544  T:        1  M: e3d5 b5e2 g3e2 c5f2 g4f2 b6f2 e4f2
viridithas-18.0.0 >> D:   3/13   S:     cp 12  N:        1194  T:        1  M: f4d5 c7d8
stormphrax-7.0.40 >> D:   3/12   S:      cp 7  N:         534  T:        0  M: e3d5 c5f2 g4f2 b5e2
 pawnocchio-1.8.1 >> D:   3/3    S:   cp -327  N:         491  T:        1  M: f4d5 b5a6
```

---

### Formatting data from "go" commands:
The "breakdown" command lets you select a metric and print out a table showing the values from each engine at each depth.

Currently supported:
- seldepth
- score
- nodes
- branching `(nodes[depth] / nodes[depth - 1])`

```
breakdown seldepth
depth viridithas-18.0.0 stormphrax-7.0.40 pawnocchio-1.8.1 horsie-1.1.6
1 13 6 2 13
2 6 5 3 8
3 13 12 3 9

breakdown score
depth viridithas-18.0.0 stormphrax-7.0.40 pawnocchio-1.8.1 horsie-1.1.6
1 13 31 -322 34
2 13 31 -371 34
3 12 7 -327 34

breakdown nodes
depth viridithas-18.0.0 stormphrax-7.0.40 pawnocchio-1.8.1 horsie-1.1.6
1 981 28 67 420
2 1044 77 272 478
3 1194 534 491 544

breakdown branching
depth viridithas-18.0.0 stormphrax-7.0.40 pawnocchio-1.8.1 horsie-1.1.6
1 1.0000 1.0000 1.0000 1.0000
2 1.0642 2.7500 4.0597 1.1381
3 1.1437 6.9351 1.8051 1.1381
```