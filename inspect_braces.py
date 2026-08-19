from pathlib import Path
from itertools import accumulate

path = Path(r'c:/Users/Nader Harrabi/Desktop/APP db/mon kiné/MonKineBlazor.Client/Pages/Cnam.razor')
text = path.read_text(encoding='utf-8')
stack = []
errors = []
for idx, ch in enumerate(text, start=1):
    if ch == '{':
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
print('first 20 errors/stack positions')
for i, e in enumerate(errors[:20], start=1):
    print(i, e)
print('---')
for i, pos in enumerate(stack[-20:], start=1):
    print('stack', i, pos)
