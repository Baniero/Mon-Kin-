from pathlib import Path
import sys
path = Path(sys.argv[1])
lines = path.read_text('utf-8').splitlines()
brace = 0
for i, line in enumerate(lines, start=1):
    for ch in line:
        if ch == '{':
            brace += 1
        elif ch == '}':
            brace -= 1
    if i <= 402 and brace < 0:
        print('negative at', i, line)
        break
    if i <= 402 and brace == 0 and line.strip() == '@code {':
        print('brace at @code start line', i, 'balance', brace)
        break
print('final up to 402', brace)
for i in range(1, 403):
    if lines[i-1].count('{') - lines[i-1].count('}') != 0:
        print(i, lines[i-1].count('{') - lines[i-1].count('}'), lines[i-1])
