DeskVibes
=========

Little helpers for Windows, no code needed. Tell it what you want in plain words:

    when I get an email from my boss, turn the edges of the screen red
    if Bitcoin drops below 60000 flash the edges orange
    remind me to stretch after 45 minutes of work
    when Stormrage realm comes back up say it out loud
    ctrl+alt+b hides everything

DeskVibes sets it up and shows you what it understood, so you can check it before saving.

Getting started
---------------
1. Unzip anywhere and run DeskVibes.exe. (It needs the free .NET 8 Desktop Runtime. Windows offers to
   install it if it's missing. The "-full" zip doesn't need it.)
2. DeskVibes lives in the tray by the clock. Click the icon to open it, right-click it for Pause, Widgets and Quit.
3. Settings: type your town (for weather). To get email alerts, add your email account. Gmail and Yahoo need an
   "app password". Classic Outlook for Windows also works while it's open.

Things it can react to
----------------------
- Email & meetings: new email (from someone / subject has a word), lots of unread email, a meeting about to start
- Time: a time of day, every N minutes, a date, when Windows starts
- You: away for a while, back at the PC, working too long without a break, lock / unlock, a hotkey, copying text
- Your PC: low battery, charger in/out, internet down/back, USB drive in/out, busy CPU, memory full, disk space low
- Apps & games: an app opens or closes, switching to an app or website, full screen, too long in an app today
- Internet: weather (rain coming, hot, cold, windy), stocks and crypto, a website down/up, WoW realm up/down, news

Things it can do
----------------
Glowing screen edges (any color, solid, pulsing or blinking), pop-up cards, a big banner, a screen flash, dimming
the screen for a break, sounds, saying something out loud, opening a website/file/app, copying text, showing the
desktop, muting, locking the PC, writing to the log.

Every rule can be limited to weekdays, weekends, work hours, when you're at the PC, not while something is full
screen (games and movies), or battery / plugged in. You can set quiet hours too.

Widgets
-------
Clock (with a second time zone), weather, stocks & crypto, PC health, countdown, focus timer, sticky note, status
board (websites and WoW realms), clipboard history, screen time, inbox, today's meetings, headlines, text.
Right-click a widget: keep on top, stay on the desktop behind your windows, lock in place, let clicks go through it,
or make it see-through.

Share
-----
"⋯ → Copy share code" on any vibe gives you a code like DV1:... that a friend can paste in.

Your data
---------
Everything is saved in %APPDATA%\DeskVibes. Passwords and keys are encrypted with your Windows account.
DeskVibes only reads who sent new emails and their subjects. It never marks anything read and never sends email.
Weather comes from Open-Meteo, prices from Yahoo Finance, realms from the Blizzard API (your own free key).
