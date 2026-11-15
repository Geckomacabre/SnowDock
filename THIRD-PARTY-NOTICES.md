# Third-party notices

SnowDock includes work adapted from the projects below.

## ManagedShell

- Project: https://github.com/cairoshell/ManagedShell (the shell library behind Cairo Desktop and RetroBar)
- Copyright: the ManagedShell contributors
- License: Apache License, Version 2.0 (https://www.apache.org/licenses/LICENSE-2.0)
- Used in: `Widgets/Tray/TrayHost.cs` and `Widgets/Tray/TrayService.cs`

What was adapted: the way the tray widget hears notification-area icons while Explorer is running (a hidden
second `Shell_TrayWnd` window kept first in z-order, which forwards every message to Explorer, plus a
`TaskbarCreated` message so apps add their icons again), the rules for which messages are forwarded and which
must be posted rather than sent, the layout of the icon data Windows sends to the tray, and the sequence of
notifications sent to an app when its icon is clicked. The code was rewritten for SnowDock and changed (for
example, Explorer still receives and answers every call).

```
Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```
