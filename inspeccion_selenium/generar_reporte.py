"""Genera el PDF institucional a partir de los resultados de Selenium."""
from __future__ import annotations

import json
from io import BytesIO
from pathlib import Path

from docx import Document
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import cm
from reportlab.platypus import Image, PageBreak, Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle

ROOT = Path(__file__).resolve().parent
EVIDENCE = ROOT / "evidencias"
OUTPUT = ROOT / "Reporte_Inspeccion_Selenium.pdf"
SOURCE_DOCX = Path.home() / "Downloads" / "Practica_6_MobileConnect.docx"


def footer(canvas, document) -> None:
    canvas.saveState()
    canvas.setFont("Helvetica", 8)
    canvas.drawString(2.5 * cm, 1.25 * cm, "Automatizacion de Pruebas con Selenium")
    canvas.drawRightString(18.5 * cm, 1.25 * cm, f"Pagina {document.page}")
    canvas.restoreState()


def cover_footer(canvas, document) -> None:
    """La portada institucional no lleva encabezado ni numeracion."""


def institutional_logo() -> Image | None:
    """Recupera el escudo usado en la portada de referencia, sin duplicarlo."""
    if not SOURCE_DOCX.exists():
        return None
    document = Document(str(SOURCE_DOCX))
    for relationship in document.part.rels.values():
        if relationship.target_ref.endswith("image2.png"):
            return Image(BytesIO(relationship.target_part.blob), width=3.2 * cm, height=3.2 * cm)
    return None


def main() -> None:
    data = json.loads((EVIDENCE / "resultados.json").read_text(encoding="utf-8"))
    styles = getSampleStyleSheet()
    styles.add(ParagraphStyle(name="Cover", parent=styles["Title"], alignment=TA_CENTER, fontSize=23, leading=29, textColor=colors.HexColor("#0066AA")))
    styles.add(ParagraphStyle(name="Heading", parent=styles["Heading2"], textColor=colors.HexColor("#0066AA"), spaceBefore=14, spaceAfter=8))
    doc = SimpleDocTemplate(str(OUTPUT), pagesize=A4, rightMargin=2.5 * cm, leftMargin=2.5 * cm, topMargin=2.2 * cm, bottomMargin=2.1 * cm)
    story = [Spacer(1, 1.3 * cm)]
    logo = institutional_logo()
    if logo:
        logo.hAlign = "CENTER"
        story += [logo, Spacer(1, 0.5 * cm)]
    story += [Paragraph("UNIVERSIDAD TECNOLOGICA DE QUERETARO", styles["Title"]), Spacer(1, 1.2 * cm), Paragraph("Nombre del proyecto:", styles["Heading"]), Paragraph("Inspeccion automatizada de SeleniumBase Demo Page", styles["Cover"]), Spacer(1, 1.5 * cm), Paragraph("Presenta:", styles["Heading"]), Paragraph("LOPEZ MOSCOSA FRANCISCO GIOVANNI", styles["Title"]), Spacer(1, 0.5 * cm), Paragraph("Matricula:", styles["Heading"]), Paragraph("2023371156", styles["Title"]), Spacer(1, 2.0 * cm), Paragraph("Santiago de Queretaro, Qro. Octubre 2026", styles["Normal"]), PageBreak()]
    story += [Paragraph("1. Objetivo", styles["Heading"]), Paragraph("Inspeccionar funcionalmente todos los elementos interactivos de la pagina de demostracion de SeleniumBase mediante Selenium WebDriver con Python, verificando su disponibilidad, valores y cambios de estado.", styles["BodyText"]), Paragraph("2. Ambiente y alcance", styles["Heading"]), Paragraph("URL inspeccionada: <link href='%s'>%s</link><br/>Navegador: Google Chrome en modo headless.<br/>Herramientas: Python 3, Selenium WebDriver y ChromeDriver administrado por Selenium Manager.<br/>Alcance: formulario, campos, SVG, boton, slider, barra de progreso, select, medidor, iFrames, radios, checkboxes, drag-and-drop, menu desplegable y enlaces." % (data["url"], data["url"]), styles["BodyText"]), Paragraph("3. Resultado de la ejecucion", styles["Heading"])]
    summary = [["Fecha", data["fecha"]], ["Casos ejecutados", str(data["total"])], ["Aprobados", str(data["aprobados"])], ["Fallidos", str(data["fallidos"])]]
    table = Table(summary, colWidths=[5 * cm, 11 * cm])
    table.setStyle(TableStyle([("GRID", (0, 0), (-1, -1), 0.5, colors.grey), ("BACKGROUND", (0, 0), (0, -1), colors.HexColor("#DCEBFA")), ("FONTNAME", (0, 0), (0, -1), "Helvetica-Bold"), ("PADDING", (0, 0), (-1, -1), 7)]))
    story += [table, Spacer(1, 0.5 * cm), Paragraph("4. Casos de prueba", styles["Heading"])]
    rows = [["No.", "Elemento o comportamiento", "Estado", "Resultado"]]
    for number, result in enumerate(data["resultados"], 1):
        rows.append([str(number), result["caso"], result["estado"], result["detalle"]])
    results_table = Table(rows, colWidths=[0.8 * cm, 7.0 * cm, 2.2 * cm, 6.0 * cm], repeatRows=1)
    results_table.setStyle(TableStyle([("GRID", (0, 0), (-1, -1), 0.35, colors.grey), ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#0066AA")), ("TEXTCOLOR", (0, 0), (-1, 0), colors.white), ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"), ("VALIGN", (0, 0), (-1, -1), "TOP"), ("FONTSIZE", (0, 0), (-1, -1), 8), ("PADDING", (0, 0), (-1, -1), 5)]))
    story += [results_table]
    verdict = "La inspeccion finalizo satisfactoriamente." if data["fallidos"] == 0 else "La inspeccion detecto casos fallidos; consulte la tabla y evidencias."
    story += [Paragraph("6. Conclusion", styles["Heading"]), Paragraph(verdict, styles["BodyText"])]
    story += [PageBreak(), Paragraph("Anexo A. Evidencias de ejecucion", styles["Heading"]), Paragraph("Cada captura fue tomada automaticamente al aprobar el caso indicado. El detalle estructurado se conserva en evidencias/resultados.json.", styles["BodyText"])]
    for number, result in enumerate(data["resultados"], 1):
        capture = EVIDENCE / f"{number:02d}.png"
        if not capture.exists():
            continue
        image = Image(str(capture))
        image._restrictSize(15.2 * cm, 15.5 * cm)
        story += [Spacer(1, 0.4 * cm), Paragraph(f"Figura A.{number}. {result['caso']} ({result['estado']}).", styles["BodyText"]), Spacer(1, 0.2 * cm), image, PageBreak()]
    doc.build(story, onFirstPage=cover_footer, onLaterPages=footer)
    print(f"PDF generado: {OUTPUT}")


if __name__ == "__main__":
    main()
