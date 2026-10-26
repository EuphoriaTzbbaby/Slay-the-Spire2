PYTHON ?= python3
SESSION ?= d1
export PYTHONPATH := src

.PHONY: doctor demo test serve capture inspect trace live overlay rollout

doctor:
	$(PYTHON) -m spirepilot doctor

demo:
	$(PYTHON) -m spirepilot demo

test:
	$(PYTHON) -m unittest discover -s tests -v

serve:
	$(PYTHON) -m spirepilot serve --port 5000

capture:
	$(PYTHON) -m spirepilot capture --port 5000 --session $(SESSION)

inspect:
	$(PYTHON) -m spirepilot inspect --state data/samples/combat_basic.json

trace:
	$(PYTHON) -m spirepilot trace --trace data/traces/basic_thunderclap.json

live:
	$(PYTHON) -m spirepilot live

overlay:
	$(PYTHON) -m spirepilot overlay

rollout:
	$(PYTHON) -m spirepilot rollout
