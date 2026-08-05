# TriCaster 460 Control Surface — Serial Protocol (reverse-engineered)

Source: `TricasterCap.pcapng` (USB capture, device address 5, FTDI FT232BM,
VID 0403 / PID 6001). ~292 s, 2092 IN messages, 798 OUT messages.

Transport: FTDI serial (handled by `ftdi_sio`). Application layer is **ASCII**.

## 1. Framing

Every logical message is an ASCII string terminated by **`\r` (0x0D)**. USB
packets do **not** align to messages — reassemble by concatenating the payload
bytes of each direction and splitting on `0x0D`.

- IN  (device→host): FTDI endpoint `0x81`, field `ftdi-ft.if_a_rx_payload`.
- OUT (host→device): FTDI endpoint `0x02`, field `ftdi-ft.if_a_tx_payload`.

Message body (before `\r`) is one of:

| Form              | Meaning                                             |
|-------------------|-----------------------------------------------------|
| *(empty)*         | Heartbeat — IN only, ~every 0.5 s while idle        |
| `~XXX`            | Handshake reply — IN only (`~009`, `~00C`)          |
| single letter     | Control command — OUT only (`I`,`V`,`T`, init blob) |
| `AA VV`           | 2 hex bytes = **address(1) + value(1)** (rows, T-bar, encoders) |
| `AA VVVVVV`       | 4 hex bytes = **address(1) + 3 value bytes** (joystick) |

Address and value are each two ASCII hex digits per byte, e.g.
`38 30 45 42 0d` = `"80EB\r"` = address `0x80`, value `0xEB`.

### 1a. Worked byte examples (field-by-field)

Each example is split into its fields. "Raw" = the actual bytes on the wire
(each is the ASCII code of one hex character); "ASCII" = the character those
bytes represent. Two ASCII hex chars = one protocol byte of address or value.
`0d` (`\r`) always terminates and is never part of the payload.

**Heartbeat** — IN, idle keepalive (~2 Hz)

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `0d` | `\r` | Terminator | Empty message = heartbeat |

**Handshake reply `~009`** — IN, answer to the `I` command

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `7e` | `~` | Prefix | Marks a handshake/ID reply |
| `30 30 39` | `009` | ID | Reply value `009` |
| `0d` | `\r` | Terminator | End of message |

**Control command `T`** — OUT, "start telemetry" (also `I`=`49`, `V`=`56`)

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `54` | `T` | Command | Single-letter control command |
| `0d` | `\r` | Terminator | End of message |

**Button row idle `10FF`** — IN, nothing pressed on row `0x10`

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `31 30` | `10` | Address | Button row `0x10` |
| `46 46` | `FF` | Value | `1111_1111` — all bits set = all buttons up |
| `0d` | `\r` | Terminator | End of message |

**Button press `10FD`** — IN, bit 1 pressed on row `0x10`

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `31 30` | `10` | Address | Button row `0x10` |
| `46 44` | `FD` | Value | `1111_1101` — bit 1 is the `0` → **bit 1 cleared = pressed** |
| `0d` | `\r` | Terminator | End of message |

**LED command `10FD`** — OUT, identical layout, opposite direction

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `31 30` | `10` | Address | LED row `0x10` (same address space as buttons) |
| `46 44` | `FD` | Value | bit 1 cleared = **light LED 1** (low = on) |
| `0d` | `\r` | Terminator | End of message |

**Button chord `10F9`** — IN, two buttons held at once

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `31 30` | `10` | Address | Button row `0x10` |
| `46 39` | `F9` | Value | `1111_1001` — bits 1 and 2 are the `0`s → **bits 1 and 2 both pressed** |
| `0d` | `\r` | Terminator | End of message |

**T-bar `80EB`** — IN, analog fader position

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `38 30` | `80` | Address | T-bar / fader channel `0x80` |
| `45 42` | `EB` | Value | Absolute position `0xEB` = 235/255 ≈ **92 %** |
| `0d` | `\r` | Terminator | End of message |

**T-bar ack `8000`** — OUT, host response while the T-bar moves

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `38 30` | `80` | Address | T-bar channel `0x80` |
| `30 30` | `00` | Value | Always `00` (ack / flow-control, see T4) |
| `0d` | `\r` | Terminator | End of message |

**Joystick `90808286`** — IN, 3 axes in one message

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `39 30` | `90` | Address | Joystick channel `0x90` |
| `38 30` | `80` | Value byte 1 | Axis X = `0x80` (centred) |
| `38 32` | `82` | Value byte 2 | Axis Y = `0x82` |
| `38 36` | `86` | Value byte 3 | Axis Z/twist = `0x86` |
| `0d` | `\r` | Terminator | End of message |

**Encoder `37FB`** — IN, relative rotary counter

| Raw | ASCII | Field | Meaning |
|-----|-------|-------|---------|
| `33 37` | `37` | Address | Encoder `0x37` |
| `46 42` | `FB` | Value | Free-running counter `0xFB`; subtract previous value for ±delta |
| `0d` | `\r` | Terminator | End of message |

**Bit-numbering reference** (value byte read LSB-first — bit 0 = `0x01`):

| pressed bit | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|-------------|---|---|---|---|---|---|---|---|
| value byte  | `FE` | `FD` | `FB` | `F7` | `EF` | `DF` | `BF` | `7F` |

"Bit N pressed" ⇔ the value has `(1<<N)` cleared. The same mapping drives LEDs on
the OUT side (cleared bit = LED on).

## 2. Handshake / session start

```
OUT  "DSsDSsDSsDSsD*nNI"   (repeated probe blob, ends the FTDI/driver bring-up)
OUT  "I\r"                 -> IN "~009\r"    (I = Identify?, reply id 009)
OUT  "V\r"                 -> IN "~00C\r"    (V = Version?,  reply id 00C)
OUT  "T\r"                 (T = start telemetry / go)
OUT  <LED init burst>      set 0x17=01, 0x16=00, then every LED row = 0xFF (all
                           off), then paint current UI state onto the LEDs.
```
After `T\r` the device free-runs: heartbeats when idle, event messages on input.
Heartbeats are **not** individually acked; there is no per-message polling.

## 3. IN direction (device → host) — inputs

| Addr(s)                         | Type            | Encoding |
|---------------------------------|-----------------|----------|
| `10`–`15`, `30`,`31`,`33`,`34`,`35`, `41`–`45` | **Button row** | 1 byte, idle `0xFF`; a pressed button **clears its bit** (bit N = 0). Release restores `0xFF`. Multiple bits can be low simultaneously (chords). 8 buttons per row. |
| `36`, `37`, `38`, `18`, `19`    | **Rotary encoder / jog** | 1 byte, free-running counter; increments on CW, decrements on CCW, wraps `0xFF`↔`0x00`. Relative: use signed delta from previous value. `36`–`38` confirmed in the capture; `18`,`19` found live (they sweep and report in lockstep). **This list is almost certainly incomplete — the board likely has more encoders we haven't exercised.** Any row that sweeps values / flips several bits at once is an encoder, not a button. |
| `32`                            | **Button/LED row** (low-confidence: only 6 IN samples). Changes are single-bit toggles (bit0 `F8↔F9`, bit5 the ±32 jump), matching button behaviour; OUT uses `32` as an LED bitmask row too. |
| `80`                            | **T-bar / fader** | 1 byte absolute position, full `0x00`–`0xFF`. Streams while moving. |
| `90`                            | **Joystick** | 3 bytes = 3 axes (X, Y, Z/twist), each 8-bit centered ~`0x80`. |

**Button bitmask confirmed:** all 16 button rows idle at `0xFF` and only ever
clear bits (≤10 distinct values each). Bit index 0 = LSB.

## 4. OUT direction (host → device) — LEDs & acks

The host controls LEDs using the **same address space and bitmask scheme** as
the button rows. **LEDs are strictly on/off — there is no colour/intensity byte**
(confirmed live: the panel only ever acts on a 1-byte bitmask; each bit = one LED
on/off). This kills the "value encodes colour" half of T3; the only remaining
open question is whether some buttons have a *second* LED on a sibling row (see T3).

| Addr(s)                         | Meaning |
|---------------------------------|---------|
| `10`–`15`, `30`–`35`, `40`–`45`, `16`, `17` | **LED row.** 1 byte bitmask; bit N = 0 turns LED N **on**, bit N = 1 turns it **off** (same polarity as the button: low = active). `0xFF` = all LEDs in row off. |
| `40`, `16`, `17`                | LED-only rows (no matching button row) — status/tally/indicator LEDs. |
| `80`                            | **T-bar ack.** Always value `00`. Emitted in bursts *in response to* IN `80` (T-bar) motion — see §5. |

**Button→LED echo confirmed:** ~11–17 ms after an IN button press on row R with
value V, the host sends OUT row R with a matching bitmask, lighting that button
(e.g. source-selection tally). The LED pattern may differ from the raw press
because selecting one source deselects others in the same row.

```
t=25.008 IN  ROW=0x10=FD   (button bit1 pressed)
t=25.019 OUT LED ROW=0x10=FD  (11 ms later: light that button)
t=25.072 IN  ROW=0x10=FF   (release)
```

## 5. Cross-direction correlation

- **Button → LED:** every physical press is echoed as an OUT LED write on the
  same row within ~10–20 ms (software-driven tally). Not a hardware autoloop —
  it's the TriCaster app deciding what to light.
- **T-bar → ack:** while the T-bar (IN `0x80`) streams position samples, the
  host emits OUT `8000\r` roughly once per 1–2 samples. Value is always `00`.
  Likely a flow-control / "position received" ack or a meter-refresh trigger.
- **Heartbeat:** IN lone `\r` ~2 Hz when idle; unacked. Only once telemetry has
  been started — a freshly opened port sends nothing at all (§7.7).
- No evidence the device needs per-message polling; after `T\r` it streams
  autonomously.

## 6. Address inventory (observed)

```
ADDR  IN#  OUT#  role
0x10   28   34   button/LED row   BOTH
0x11   22   25   button/LED row   BOTH
0x12   16   10   button/LED row   BOTH
0x13   16   10   button/LED row   BOTH
0x14   16   10   button/LED row   BOTH
0x15   18   10   button/LED row   BOTH
0x16    0    1   LED row          OUT-only
0x17    0    1   LED row          OUT-only
0x30   28   37   button/LED row   BOTH
0x31   16   29   button/LED row   BOTH
0x32    6   17   AMBIGUOUS        BOTH   (IN looked encoder-ish, OUT is LED bitmask)
0x33   26   36   button/LED row   BOTH
0x34    8   27   button/LED row   BOTH
0x35   18   31   button/LED row   BOTH
0x36   25    0   rotary encoder   IN-only
0x37   37    0   rotary encoder   IN-only
0x38   24    0   rotary encoder   IN-only
0x40    0   43   LED row          OUT-only
0x41   54   62   button/LED row   BOTH
0x42   22   53   button/LED row   BOTH
0x43   20   35   button/LED row   BOTH
0x44   16   23   button/LED row   BOTH
0x45   32   23   button/LED row   BOTH
0x80  381  277   T-bar / ack      BOTH
0x90  929    0   joystick 3-axis  IN-only
```

Note: `0x18`,`0x19` do **not** appear above — they weren't present in the pcapng
and were only seen in a later live session (encoders/jog). The inventory from a
single capture is therefore **not exhaustive**; treat any address list here as
"seen so far," not "all there is."

So: **~16 rows × 8 = up to 128 buttons**, **at least 5 rotary encoders/jog**
(`36`–`38`, `18`, `19`, and probably more not yet exercised; + maybe `32`),
**1 T-bar**, **1 three-axis joystick**, plus dedicated indicator-LED rows.

## 7. Open questions (need labeled action log / video)

1. **Physical mapping** of each (row, bit) to a named button — requires the video.
2. **`0x32`**: leaning button/LED row (single-bit toggles), but only 6 IN
   samples — confirm by pressing whatever is on that row deliberately.
3. **LED colour — mostly RESOLVED:** LEDs are **strictly on/off, one bit per
   LED, no colour/intensity byte** (confirmed live). The only residual question
   is whether a physical button ever has *two* single-colour LEDs on two rows
   (red on `0x1x` + green on `0x3x` at the same bit). Test with
   `tricaster_live.py --probe`. See T3 in §9.
4. **OUT `8000`** exact role — pure ack, flow control, or does it also drive a
   T-bar/meter LED? Move the T-bar and note any LED/meter response.
5. **Encoder direction & detents** — confirm CW = increment, and counts per detent.
6. **`I`/`V`/`T`** command semantics and the `~009`/`~00C` reply meaning
   (firmware/hardware id?). Also the `DSsD*nN` init blob.
7. **Handshake requirement — RESOLVED:** the panel is **completely silent until
   spoken to.** Opening the port and waiting produces nothing at all — no
   heartbeat, no telemetry — so `T\r` is **mandatory** to start the stream, and
   the ~2 Hz idle heartbeat in §1 only runs *after* it. `I\r` and `V\r` are
   answered immediately (`~009` / `~00C`) without starting telemetry, which makes
   `I\r` the cheap way to test whether a port is a control surface. Confirmed
   live 2026-08-04 against `cu.usbserial-FTYUVF8W`; used by
   `PanelDiscovery.Probe`.
8. **Joystick axis order** (which byte is X/Y/twist) and whether center is
   exactly `0x80` after calibration.

## 8. Tooling in this repo

- `decode.py` — reassembles both directions and prints:
  - `python3 decode.py`            full bidirectional event log
  - `python3 decode.py --summary`  per-address frequency/value tables
  - `python3 decode.py --presses`  button-labeling worksheet
- `decoded_log.txt`     — full decoded event log
- `button_worksheet.txt`— press-onset events to annotate against the video
- `rx_raw.tsv`,`tx_raw.tsv` — raw tshark payload dumps (regenerate below)

Regenerate raw dumps from a pcapng:
```
tshark -r CAP.pcapng -Y ftdi-ft.if_a_rx_payload -T fields \
  -e frame.time_relative -e ftdi-ft.if_a_rx_payload > rx_raw.tsv
tshark -r CAP.pcapng -Y ftdi-ft.if_a_tx_payload -T fields \
  -e frame.time_relative -e ftdi-ft.if_a_tx_payload > tx_raw.tsv
```

## 9. Theories & uncertain interpretations

Everything below is **speculation**, not established fact. It's recorded here so
we don't mistake a working guess for a confirmed conclusion. Each theory lists
what we've seen, competing explanations, and the test that would settle it.

### T1. Row `0x17` and its non-`0xFF` idle state
**Observation:** In the original pcapng, `0x17` appeared only *once*, as an OUT
LED command (`17=01`). It was never sent by the device. In a later **live**
session, `0x17` arrived on IN sitting at `0xEC` (bits 0,1,4 low) with bit 2
toggling press/release on top.

Competing explanations for the persistently-low bits 0,1,4:
- **(a) Resting hardware state** — those positions are latching switches, a
  rotary selector, or unpopulated inputs that read as 0 at rest.
- **(b) Buttons physically held** at the moment the monitor started (e.g. a hand
  resting on the panel, or a mode/shift button held during the session).
- **(c) A different meaning entirely for row `0x17`** — e.g. it's not a button
  row at all but a status/config byte (DIP settings, panel variant id, a mode
  register) that merely *looks* like a bitmask.

We currently lean (a), but **we have not confirmed it.** The "initial" baseline
the live monitor prints is just the first sample — it cannot distinguish (a)
from (b) from a single reading.
**Test:** at panel power-up with hands off, watch `0x17`'s value; then flip every
physical switch/selector near it and see which bits move. If bits 0,1,4 never
change no matter what you press, they're likely fixed config (c); if they change
when you flip a latching control, it's (a); if they were only low because you
were holding something, they'll go high (release) on their own.

### T2. Is "idle = 0xFF" actually universal for button rows?
**Observation:** all 16 button rows in the pcapng idled at `0xFF`; `0x17` (live)
did not.
- **(a)** `0xFF` idle is the norm and `0x17` is a special non-button row (see T1c).
- **(b)** `0xFF` idle is *not* guaranteed; any row can rest with bits low, and we
  only saw all-`0xFF` because those rows happened to be untouched/unlatched.
Implication for the driver: edge detection must be **relative to the previous
value**, never assume `0xFF`. (The live monitor now does this; `decode.py` still
assumes `0xFF` and shares the latent bug — see §8.)
**Test:** enumerate the idle value of *every* row at power-up, hands off.

### T3. `0x16` is probably the same story as `0x17`
`0x16` also appeared OUT-only (`16=00`) in the init burst and never on IN.
By symmetry with `0x17` it may also be a bidirectional button/indicator row that
simply wasn't exercised in the capture. **Unconfirmed** — it may equally be a
pure indicator (LED-only) row. Same test as T1.

### T4. OUT `8000` — ack vs. something else
**Observation:** OUT `8000\r` fires in bursts that track IN `0x80` (T-bar) motion,
value always `00`.
- **(a)** Flow-control / "position received" ack for the analog channel.
- **(b)** A meter/position **write** back to the panel (drive a T-bar LED or
  motorized position) that just happens to be `00` here because nothing was lit.
- **(c)** Coincidence of timing — the host polls `80` on a timer that only runs
  while its UI shows the T-bar, unrelated to the inbound samples.
**Test:** in the TriCaster app, trigger something that would move a T-bar
indicator/meter and see if OUT `80` ever carries a non-`00` value.

### T5. The `~009` / `~00C` replies and the `I`/`V`/`T` commands
Guessed as Identify / Version / start-Telemetry with reply ids. Purely nominal.
- The `DSsD*nN` blob before them is unexplained (FTDI bring-up? a magic knock?).
- Unknown whether `T\r` alone starts streaming or the full LED-init burst is
  required first. The live app sends only `I`/`V`/`T`; if the panel doesn't
  stream, the burst may be mandatory.
**Test:** send just `T\r` to a freshly-enumerated panel and see if buttons stream.

### T6. `0x32` — button row, encoder, or both?
Leaned "button/LED row" from 6 samples (single-bit toggles) plus OUT using it as
an LED bitmask. But sample count is tiny and it could still be an encoder whose
counts happened to land on single-bit deltas. **Low confidence.**
**Test:** deliberately press vs. rotate whatever sits at that address.

### T7. How many encoders/jog controls are there really?
**Observation:** `36`–`38` (capture) + `18`,`19` (live) behave as sweeping
counters, not buttons. `18`/`19` report in lockstep — possibly one dual-phase
control (jog/shuttle) reported on two addresses, or two ganged encoders.
- The full set is **unknown**; more encoders probably exist on rows we haven't
  turned yet. A single capture/session under-samples the hardware.
- Practical impact: any encoder mis-seen as a button row will spew multi-bit
  "presses" (and, in the reactive monitor, LED storms). The monitor keeps an
  editable `ENCODERS` set (`tricaster_live.py`) — add a row there when a control
  is found to sweep. **Do not assume the list is complete.**
**Test:** turn every rotary/jog control on the panel and record which addresses
sweep; add each to `ENCODERS`. Also check whether `18`/`19` are one control or two.

### T8. Does any button have a second single-colour LED on a sibling row?
Follows from T3 now that colour-as-value is ruled out. Rows come in bands
(`0x1x`, `0x3x`, `0x4x`); a button might drive one LED in one band and a second
(different colour) LED in another band at the same bit.
- **(a)** No — each button has exactly one LED on its own row (`self`); the bands
  are just independent button/LED groups.
- **(b)** Yes — pressing/​lighting a button's bit on a sibling band lights a
  *second* LED on the *same physical key* in a different colour.
**Test:** `tricaster_live.py --probe` and watch a single key. If the sibling
write only ever lights *other* keys, it's (a) and there is no second plane.

### How to promote a theory to fact
Each test above produces a labeled observation. When one is confirmed, move the
statement up into the relevant numbered section (§3–§5) and delete it here, so
this section only ever holds things we *don't* yet know.
