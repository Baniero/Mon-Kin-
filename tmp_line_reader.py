from pathlib import Path
import sys
if len(sys.argv) < 4:
    print('Usage: tmp_line_reader.py <file> <start> <end>')
    sys.exit(1)
path = Path(sys.argv[1])
start = int(sys.argv[2])
end = int(sys.argv[3])
lines = path.read_text('utf-8').splitlines()
for i in range(start-1, min(end, len(lines))):
    print(f'{i+1}: {lines[i]}')
