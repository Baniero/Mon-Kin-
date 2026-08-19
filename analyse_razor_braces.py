from pathlib import Path

path = Path(r'c:/Users/Nader Harrabi/Desktop/APP db/mon kiné/MonKineBlazor.Client/Pages/Cnam.razor')
text = path.read_text(encoding='utf-8')
stack = []
errors = []

in_string = False
string_char = None
escaped = False
for idx, ch in enumerate(text, start=1):
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
            stack.append(idx)
        elif ch == '}':
            if stack:
                stack.pop()
            else:
                errors.append(('extra_close', idx))

print('total_open', text.count('{'), 'total_close', text.count('}'))
print('extra_close_count', sum(1 for e in errors if e[0] == 'extra_close'))
if stack:
    print('unclosed_count', len(stack), 'last_unclosed', stack[-1])
else:
    print('all braces balanced')

if errors:
    print('extra closes:', errors[:20])

# Show line around last unclosed if any
if stack:
    last = stack[-1]
    line = text.count('\n', 0, last) + 1
    col = last - text.rfind('\n', 0, last)
    lines = text.splitlines()
    start = max(0, line-5)
    end = min(len(lines), line+5)
    print('context around last unclosed:')
    for i in range(start, end):
        print(f'{i+1}: {lines[i]!r}')
