from pathlib import Path

path = Path(r'c:/Users/Nader Harrabi/Desktop/APP db/mon kiné/MonKineBlazor.Client/Pages/Cnam.razor')
text = path.read_text(encoding='utf-8')
lines = text.splitlines()
code_start = None
for i, line in enumerate(lines):
    if line.strip() == '@code {':
        code_start = i
        break
if code_start is None:
    raise SystemExit('No @code block found')
code_text = '\n'.join(lines[code_start+1:])

# Strip string literals and char literals from code_text
clean = []
in_string = False
string_char = None
escaped = False
for ch in code_text:
    if in_string:
        if escaped:
            escaped = False
            clean.append(' ')
        elif ch == '\\':
            escaped = True
            clean.append(' ')
        elif ch == string_char:
            in_string = False
            clean.append(' ')
        else:
            clean.append(' ')
    else:
        if ch in ('"', "'"):
            in_string = True
            string_char = ch
            clean.append(' ')
        else:
            clean.append(ch)
clean_text = ''.join(clean)

balance = 0
for i, line in enumerate(clean_text.splitlines(), start=code_start+2):
    delta = line.count('{') - line.count('}')
    if delta != 0:
        balance += delta
        print(f'{i}: delta={delta}, balance={balance}, {line}')
print('final balance', balance)
