# EVision vendor protocol notes (USB 320F:505B)

Notes on the vendor HID protocol as used by OpenEVision. The command set comes from OpenRGB's EVision keyboard
controller; the details below were checked on a full-size ISO keyboard reporting USB `320F:505B` (126 LEDs).
Other EVision models may differ. Model-specific values live in `app/Device/DeviceProfile.cs`.

## Transport

- Vendor HID interface with usage page `0xFF1C`, 64-byte input and output reports, report id `4`.
- Packet layout: byte 0 = report id, bytes 1–2 = checksum (sum of bytes 3..63, little endian), byte 3 = command,
  byte 4 = payload size, bytes 5–6 = offset (little endian), byte 7 = status (0 = OK in replies), payload from byte 8
  (up to 56 bytes per packet).
- Every open handle on the interface receives every input report. OpenEVision flushes the HID input queue before each
  request and matches the reply on command + offset; without that a stale reply can shift a multi-packet read.
  Transactions are serialized across processes with the named mutex `Local\OpenEVision.Device`.

## Commands

| Cmd | Use |
|---|---|
| `0x01` / `0x02` | begin / end a configuration write |
| `0x05` / `0x06` | config read / write: effect header `0x00–0xBF`, key table at `0xC0` |
| `0x10` / `0x11` | custom (per-key) colors in flash, order `row * 21 + slot` |
| `0x12` / `0x13` | live colors in RAM, in key-table order / end live mode |

## Memory regions

- **Effect header** (config `0x00–0xBF`): byte 0 mode, 1 brightness (0–4), 2 speed (0 = fastest, 5 = slowest),
  3 direction, 4 random-color flag, 5–7 RGB color.
- **Key table** (config `0xC0`, 128 entries of 3 bytes): `02 02 usage` key (HID usage), `02 01 bit` modifier,
  `03 lo hi` consumer/media usage, `05 ..` Fn. Writing an entry remaps that key; remaps are stored in flash and survive
  unplugging. Copies of the table at `0x2C0` and `0x4C0` have an unknown purpose and are never written.
- **Custom colors** (custom memory, 126 entries of 3 bytes): row-major `row * 21 + slot`, the same matrix OpenRGB uses
  for EVision keyboards. Writes show immediately but take about 22 ms per packet (flash), so they are not usable for
  animation.
- **Live colors** (`0x12`, 128 entries of 3 bytes): RAM only, in the same order as the key table (entry index = live
  LED index). The keyboard drops live colors after a short idle time, so frames are re-sent at least every 150 ms.
  OpenEVision decodes the live order from the factory key table, so remapped keys still light at their physical place.
- Config `0x0F` is described by OpenRGB as the polling rate; on this model it reads `00` while the USB endpoint
  descriptor reports a 1 ms interval (1000 Hz). It is treated as unknown and never written; the polling rate shown in
  the app is read from the endpoint descriptor.
- Config `0x700` and above changes on its own; purpose unknown, never written.

## Write guard

`HidKeyboard.WriteAllowed` only lets these through: config writes inside the effect header or the key table, custom
color writes inside the 126-LED table, and live color writes inside the 128-entry table. Everything else throws before a
packet is sent.

## Keys

- Fn never reaches the PC, so it cannot be detected by the typing effects and is not remappable.
- AltGr is reported to Windows as a synthetic left Ctrl followed by right Alt; the keyboard hook ignores the synthetic
  Ctrl (scan code `0x21D`).
