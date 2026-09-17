# Prometheus export (removed)

The collector no longer serves a `/metrics` scrape endpoint, and the `exporters` config
block is gone. It pushes to a Prometheus remote-write endpoint instead.

See [Metrics push](Metrics-Push).
