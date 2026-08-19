from pathlib import Path
import sys
path = Path(sys.argv[1])
start = int(sys.argv[2])
end = int(sys.argv[3])
lines = path.read_text('utf-8').splitlines()
text = '\n'.join(lines[start-1:end])
balance = 0
in_string = False
string_char = None
escaped = False
for i, line in enumerate(lines[start-1:end], start):
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
    print(f'{i}: balance={balance} | {line}')
print('FINAL BALANCE', balance)
