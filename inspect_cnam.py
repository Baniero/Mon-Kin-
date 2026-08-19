from pathlib import Path
path = Path(r'c:/Users/Nader Harrabi/Desktop/APP db/mon kiné/MonKineBlazor.Client/Pages/Cnam.razor')
lines = path.read_text(encoding='utf-8').splitlines()
for i in range(820, 960):
    print(f'{i+1}: {lines[i]}')
