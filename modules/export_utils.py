from datetime import datetime


def _safe_reportlab_import():
    try:
        from reportlab.lib.pagesizes import A4
        from reportlab.pdfgen import canvas
        return A4, canvas, None
    except Exception as exc:
        return None, None, exc


def export_simple_table_pdf(file_path, title, headers, rows):
    A4, canvas, err = _safe_reportlab_import()
    if err:
        raise RuntimeError("ReportLab est requis pour l'export PDF. Installez 'reportlab'.") from err

    c = canvas.Canvas(file_path, pagesize=A4)
    width, height = A4

    y = height - 40
    c.setFont("Helvetica-Bold", 14)
    c.drawString(40, y, title)

    y -= 24
    c.setFont("Helvetica", 9)
    c.drawString(40, y, f"Exporté le: {datetime.now().strftime('%Y-%m-%d %H:%M')}")

    y -= 24
    c.setFont("Helvetica-Bold", 9)
    x_positions = [40]
    col_width = max(90, int((width - 80) / max(1, len(headers))))
    for i in range(1, len(headers)):
        x_positions.append(x_positions[-1] + col_width)

    for i, header in enumerate(headers):
        c.drawString(x_positions[i], y, str(header)[:24])

    y -= 14
    c.setFont("Helvetica", 8)
    for row in rows:
        if y < 40:
            c.showPage()
            y = height - 40
            c.setFont("Helvetica", 8)
        for i, value in enumerate(row):
            c.drawString(x_positions[i], y, str(value)[:28])
        y -= 12

    c.save()
