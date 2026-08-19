from pathlib import Path
import sys
path = Path(sys.argv[1])
text = path.read_text('utf-8')
lines = text.splitlines()
balance = 0
in_string = False
string_char = None
escaped = False
for i, line in enumerate(lines, start=1):
    for ch in line:
        if in_string:
            if escaped:
                escaped = False
            elif ch == '\\':
                escaped = True
            elif ch == string_char:
                in_string = False
        else:
            if ch in ('"', "'"):
                in_string = True
                string_char = ch
            elif ch == '{':
                balance += 1
            elif ch == '}':
                balance -= 1
    if i <= 402:
        if balance != 0:
            print(f'{i}: balance={balance} line={line}')
print('final balance up to 402:', balance)
print('line 402 content:', lines[401] if len(lines) >= 402 else '<missing>')
