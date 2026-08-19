from pathlib import Path

path = Path(r'c:/Users/Nader Harrabi/Desktop/APP db/mon kiné/MonKineBlazor.Client/Pages/Cnam.razor')
text = path.read_text(encoding='utf-8')
lines = text.splitlines()
print('Total lines', len(lines))

balance = 0
for i, line in enumerate(lines, 1):
    delta = line.count('{') - line.count('}')
    if delta != 0:
        print(f'{i}: delta={delta}, balance_after={balance+delta}, line={line}')
    balance += delta
print('final balance', balance)

# report around the last mismatch if any
if balance != 0:
    for i in range(max(1, len(lines)-50), len(lines)+1):
        print(f'{i}: {lines[i-1]}')
