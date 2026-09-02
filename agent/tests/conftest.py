from pydantic_ai import models

# A missed override must fail instead of reaching any real model provider.
models.ALLOW_MODEL_REQUESTS = False
