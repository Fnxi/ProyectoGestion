"""Inspeccion funcional de todos los controles interactivos de SeleniumBase Demo Page."""
from __future__ import annotations

import json
import sys
import time
from argparse import ArgumentParser
from datetime import datetime
from pathlib import Path

from selenium import webdriver
from selenium.common.exceptions import WebDriverException
from selenium.webdriver import ActionChains
from selenium.webdriver.common.by import By
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import Select, WebDriverWait

URL = "https://seleniumbase.io/demo_page"
ROOT = Path(__file__).resolve().parent
EVIDENCE = ROOT / "evidencias"


def main() -> int:
    parser = ArgumentParser(description="Inspeccion funcional de SeleniumBase Demo Page.")
    parser.add_argument("--visible", action="store_true", help="Muestra Chrome durante la ejecucion.")
    parser.add_argument("--pausa", type=float, default=0, help="Segundos de pausa despues de cada caso.")
    args = parser.parse_args()
    EVIDENCE.mkdir(exist_ok=True)
    options = webdriver.ChromeOptions()
    if not args.visible:
        options.add_argument("--headless=new")
    options.add_argument("--window-size=1440,1600")
    options.add_argument("--disable-gpu")
    options.add_argument("--no-sandbox")
    driver = webdriver.Chrome(options=options)
    wait = WebDriverWait(driver, 12)
    results: list[dict[str, str]] = []

    def snapshot(name: str) -> None:
        driver.save_screenshot(str(EVIDENCE / f"{name}.png"))

    def check(name: str, action) -> None:
        try:
            action()
            results.append({"caso": name, "estado": "APROBADO", "detalle": "Comportamiento verificado."})
            snapshot(f"{len(results):02d}")
            time.sleep(args.pausa)
        except Exception as error:  # Registra el fallo y permite terminar la inspeccion.
            results.append({"caso": name, "estado": "FALLIDO", "detalle": f"{type(error).__name__}: {error}"})
            snapshot(f"{len(results):02d}_fallo")
            time.sleep(args.pausa)

    try:
        driver.get(URL)
        wait.until(EC.visibility_of_element_located((By.ID, "myTable")))

        def page_load() -> None:
            assert driver.title == "Web Testing Page"
            assert driver.find_element(By.ID, "myForm").is_displayed()
            assert driver.find_element(By.ID, "myTable").is_displayed()
            assert driver.find_element(By.TAG_NAME, "h1").text == "Demo Page"
            assert driver.find_element(By.ID, "mySVG").is_displayed()

        check("Carga, encabezados, formulario, tabla y SVG", page_load)

        def text_fields() -> None:
            field = driver.find_element(By.ID, "myTextInput")
            field.send_keys("Prueba Selenium")
            area = driver.find_element(By.ID, "myTextarea")
            area.send_keys("Texto de inspeccion")
            placeholder = driver.find_element(By.ID, "placeholderText")
            placeholder.send_keys("Valor de prueba")
            assert field.get_attribute("value") == "Prueba Selenium"
            assert area.get_attribute("value") == "Texto de inspeccion"
            assert placeholder.get_attribute("placeholder") == "Placeholder Text Field"
            assert driver.find_element(By.ID, "myTextInput2").get_attribute("value") == "Text..."
            readonly = driver.find_element(By.ID, "readOnlyText")
            assert readonly.get_attribute("readonly") is not None
            assert readonly.get_attribute("value") == "The Color is Green"

        check("Campos de texto, area, placeholder, prellenado y solo lectura", text_fields)

        def button_and_svg() -> None:
            driver.find_element(By.ID, "svgRect").click()
            button = driver.find_element(By.ID, "myButton")
            button.click()
            assert button.text == "Click Me (Purple)"
            assert driver.find_element(By.ID, "pText").text == "This Text is Purple"
            assert driver.find_element(By.ID, "readOnlyText").get_attribute("value") == "The Color is Purple"
            button.click()
            assert button.text == "Click Me (Green)"
            assert driver.find_element(By.ID, "pText").text == "This Text is Green"

        check("SVG y boton con alternancia de color", button_and_svg)

        def slider() -> None:
            control = driver.find_element(By.ID, "mySlider")
            driver.execute_script("arguments[0].value = 80; arguments[0].dispatchEvent(new Event('input', {bubbles: true}));", control)
            assert driver.find_element(By.ID, "progressBar").get_attribute("value") == "80"
            assert driver.find_element(By.ID, "progressLabel").text == "Progress Bar: (80%)"

        check("Slider y barra de progreso", slider)

        def select_dropdown() -> None:
            dropdown = Select(driver.find_element(By.ID, "mySelect"))
            for value in ("25%", "50%", "75%", "100%"):
                dropdown.select_by_value(value)
                # Chrome normaliza el atributo value="1.0" como "1".
                expected_meter = int(value[:-1]) / 100
                actual_meter = float(driver.find_element(By.ID, "meterBar").get_attribute("value"))
                assert actual_meter == expected_meter
                assert driver.find_element(By.ID, "meterLabel").text == f"HTML Meter: ({value})"

        check("Select y medidor en sus cuatro opciones", select_dropdown)

        def frames() -> None:
            assert driver.find_element(By.ID, "myFrame1").is_displayed()
            driver.switch_to.frame("myFrame2")
            assert "iFrame Text" in driver.find_element(By.TAG_NAME, "body").text
            driver.switch_to.default_content()
            driver.switch_to.frame("myFrame3")
            checkbox = driver.find_element(By.ID, "checkBox6")
            checkbox.click()
            assert checkbox.is_selected()
            driver.switch_to.default_content()

        check("Los tres iFrames y checkbox interno", frames)

        def radios_and_checkboxes() -> None:
            radio_1 = driver.find_element(By.ID, "radioButton1")
            radio_2 = driver.find_element(By.ID, "radioButton2")
            assert radio_1.is_selected()
            radio_2.click()
            assert radio_2.is_selected() and not radio_1.is_selected()
            for identifier in ("checkBox2", "checkBox3", "checkBox4"):
                box = driver.find_element(By.ID, identifier)
                box.click()
                assert box.is_selected()
            assert driver.find_element(By.ID, "checkBox5").is_selected()
            reveal = driver.find_element(By.ID, "checkBox1")
            reveal.click()
            assert driver.find_element(By.CSS_SELECTOR, "tr.hidden_row").is_displayed()

        check("Radios, checkboxes y revelado de fila", radios_and_checkboxes)

        def drag_and_drop() -> None:
            source = driver.find_element(By.ID, "logo")
            target = driver.find_element(By.ID, "drop2")
            ActionChains(driver).drag_and_drop(source, target).perform()
            assert driver.find_element(By.ID, "logo").find_element(By.XPATH, "..") == target

        check("Arrastrar imagen entre zonas de destino", drag_and_drop)

        def hover_dropdown() -> None:
            menu = driver.find_element(By.ID, "myDropdown")
            ActionChains(driver).move_to_element(menu).perform()
            option = wait.until(EC.element_to_be_clickable((By.ID, "dropOption2")))
            option.click()
            assert driver.find_element(By.TAG_NAME, "h3").text == "Link Two Selected"

        check("Menu desplegable por hover y opcion", hover_dropdown)

        def links() -> None:
            links_to_check = {
                "myLink1": "https://seleniumbase.com",
                "myLink2": "https://github.com/seleniumbase/SeleniumBase",
                "myLink3": "https://seleniumbase.io",
                "myLink4": "https://seleniumbase.io/demo_page/",
            }
            for identifier, expected_url in links_to_check.items():
                link = driver.find_element(By.ID, identifier)
                assert link.get_attribute("href").rstrip("/") == expected_url.rstrip("/")
                link.click()
                wait.until(lambda d: d.current_url.rstrip("/") == expected_url.rstrip("/"))
                driver.back()
                wait.until(EC.presence_of_element_located((By.ID, "myTable")))

        check("Cuatro enlaces externos e internos", links)
    except WebDriverException as error:
        results.append({"caso": "Ejecucion", "estado": "FALLIDO", "detalle": f"WebDriverException: {error}"})
    finally:
        finished = datetime.now().astimezone().isoformat(timespec="seconds")
        driver.quit()

    approved = sum(item["estado"] == "APROBADO" for item in results)
    report = {"url": URL, "fecha": finished, "total": len(results), "aprobados": approved, "fallidos": len(results) - approved, "resultados": results}
    (EVIDENCE / "resultados.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Resultado: {approved}/{len(results)} casos aprobados. Evidencias: {EVIDENCE}")
    return 0 if approved == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
