# GRBL Lathe Sender

GRBL Lathe Sender is a simple, touch-friendly interface for driving and controlling a GRBL-powered lathe.

The goal of the project is to make common lathe tasks easier from a shop-floor UI:

- machine connection and status monitoring
- large DRO readouts for X and Z
- touch-friendly jog controls
- work offset and touch-off helpers
- G-code loading, preview, and sending
- basic tool management and tool-offset handling

This project is still in active design and development.

It should not be considered stable yet.

Features, workflows, and tool handling behavior may continue to change as the interface and control model are refined.

## Keyboard Controls

When `Keyboard Control` is enabled in the app:

- `Left Arrow`: jog negative on the selected axis
- `Right Arrow`: jog positive on the selected axis
- `Up Arrow`: increase the selected step size
- `Down Arrow`: decrease the selected step size
- `A`: switch keyboard control between `X` and `Z`
- `,`: reduce the active axis feed rate by `10`
- `.`: increase the active axis feed rate by `10`

The current keyboard-controlled axis is shown in both single-screen and dual-screen mode with a green LED indicator next to `X` or `Z`.
