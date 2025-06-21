# Shared utilities for the Bonjwa pipeline
import os
import logging

def setup_logging(name: str, logfile: str) -> logging.Logger:
    """
    Configure and return a logger that writes to 'logfile' and stdout.

    Ensures handlers are only added once per logger name.
    """
    os.makedirs(os.path.dirname(logfile), exist_ok=True)
    handler = logging.FileHandler(logfile, encoding='utf-8')
    fmt = logging.Formatter('%(asctime)s %(levelname)s %(message)s')
    handler.setFormatter(fmt)

    logger = logging.getLogger(name)
    if not logger.handlers:
        logger.setLevel(logging.INFO)
        logger.addHandler(handler)
        console = logging.StreamHandler()
        console.setFormatter(fmt)
        logger.addHandler(console)

    return logger