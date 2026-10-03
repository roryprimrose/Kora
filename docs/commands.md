# Voice and typed commands

Voice and typed input use the same exact built-in phrases. Voice input may use
the configured assistant name as a prefix. Punctuation and capitalization do
not matter, but Kora does not interpret arbitrary natural-language variations.

If the assistant is renamed, replace Kora with the configured name. The old
name is not retained as a hidden alias.

## Windows and Kora

- **show Kora**
- **open Kora**
- **show your window**
- **hide Kora**
- **hide your window**
- **exit Kora**
- **quit Kora**
- **close the Kora application**
- **restart Kora**
- **restart your application**

Application restart restarts Kora. It is different from restarting Windows.

## Settings, setup, and documentation

- **open settings**
- **show Kora settings**
- **open setup**
- **configure Kora**
- **show what you need**
- **open documentation**
- **show documentation**
- **show the user guide**
- **help**
- **what can you do**
- **show supported commands**

## Information and task control

- **what version are you running**
- **show your version**
- **what are you currently working on**
- **what are you doing**
- **cancel task**
- **cancel current task**
- **stop**
- **stop speaking**

## Windows session

- **lock the machine**
- **lock my computer**
- **lock windows**

Locking is a real local action. Kora closes microphone capture before asking
Windows to lock.

## Protected power proposals

- **shut down the computer**
- **shut down this machine**
- **power off the computer**
- **restart the computer**
- **reboot this machine**
- **restart windows**
- **cancel shutdown**
- **cancel that shutdown**
- **cancel computer restart**
- **cancel that reboot**
- **what power action is pending**
- **are you about to restart the computer**

Shutdown and Windows restart are proposals in the current release. Kora shows
the recognized request but does not send a power operation to Windows.

## When a command does not match

Kora shows the normalized transcript and a visible not-recognized response. Use
**what can you do** or open this Documentation window to review exact phrases.
