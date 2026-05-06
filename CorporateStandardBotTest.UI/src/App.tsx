import { useEffect, useRef, useState, type SubmitEvent } from "react"

import { useApiClient } from "@/api"
import type { components } from "@/api/schema"
import { ChatMessage } from "@/components/ChatMessage"
import { PromptInput } from "@/components/PromptInput"
import { Button } from "@/components/ui/button"
import { LoadingMessage } from "@/components/LoadingMessage"
import { SquarePenIcon } from "lucide-react"
import { Layout } from "./components/Layout"
import { AuthenticatedTemplate, MsalAuthenticationTemplate, UnauthenticatedTemplate } from "@azure/msal-react"
import { InteractionType } from "@azure/msal-browser";
import { authRequest } from "./auth/config"

type AiChatMessage = components["schemas"]["AiChatMessage"]
type AiReasoningEffort = components["schemas"]["AiReasoningEffort"]

const USER_ROLE = "User"

function getErrorMessage(error: unknown) {
  if (error instanceof Error && error.message) {
    return error.message
  }

  if (typeof error === "object" && error !== null) {
    const detail = "detail" in error ? error.detail : undefined
    if (typeof detail === "string" && detail) {
      return detail
    }
  }

  return "Something went wrong while generating a response."
}

function UserMessage({ message }: { message: string }) {
  return (
    <div className="flex justify-end">
      <article className="max-w-[85%] rounded-2xl bg-primary px-4 py-3 text-sm leading-6 text-primary-foreground shadow-sm whitespace-pre-wrap">
        {message}
      </article>
    </div>
  )
}

function AuthenticatedApp() {
  const $api = useApiClient()
  const [threadId, setThreadId] = useState<string>(() => crypto.randomUUID())
  const [messages, setMessages] = useState<AiChatMessage[]>([])
  const [prompt, setPrompt] = useState("")
  const [submitError, setSubmitError] = useState<{ prompt: string | null, error: string } | null>(null)
  const pendingPromptRef = useRef<string | null>(null)
  const scrollAnchorRef = useRef<HTMLDivElement | null>(null)
  const lastUserMessageRef = useRef<HTMLDivElement | null>(null)
  const [reasoning, setReasoning] = useState<AiReasoningEffort>("None")
  const mutation = $api.useMutation("post", "/api/chat/complete")

  useEffect(() => {
    if (mutation.isPending)
      scrollAnchorRef.current?.scrollIntoView({ block: "end", behavior: "smooth" })
    else
      lastUserMessageRef.current?.scrollIntoView({ block: "start", behavior: "smooth" })
  }, [mutation.isPending])

  const handleSubmit = (e: SubmitEvent<HTMLFormElement>) => {
    e.preventDefault()

    if (mutation.isPending) {
      return
    }

    const formData = new FormData(e.currentTarget)
    const promptValue = formData.get("prompt")
    const content = typeof promptValue === "string" ? promptValue.trim() : ""

    if (!content) {
      return
    }

    const nextMessages = [...messages, { messageId: crypto.randomUUID(), role: USER_ROLE, content } as const]

    pendingPromptRef.current = content
    setSubmitError(null)
    setMessages(nextMessages)
    setPrompt("")

    mutation.mutate(
      { body: { chat: { threadId, messages: nextMessages }, reasoningEffort: reasoning } },
      {
        onSuccess: (assistantMessage) => {
          pendingPromptRef.current = null
          setMessages((currentMessages) => [...currentMessages, assistantMessage])
        },
        onError: (error) => {
          const failedPrompt = pendingPromptRef.current
          pendingPromptRef.current = null
          if (failedPrompt) {
            setPrompt(failedPrompt)
          }
          setSubmitError({
            prompt: failedPrompt,
            error: getErrorMessage(error)
          })
        },
      }
    )
  }

  const resetChat = () => {
    setThreadId(crypto.randomUUID())
    setMessages([])
    setPrompt("")
    setSubmitError(null)
    pendingPromptRef.current = null
    mutation.reset()
  }

  const lastUserMessageIdx = messages.findLastIndex((message) => message.role === USER_ROLE)

  return <Layout headerOutlet={messages.length > 0 && (
    <Button className="cursor-pointer" onClick={() => resetChat()}>
      <SquarePenIcon /> New Chat
    </Button>
  )}>

    <section className="min-h-0 flex-1 overflow-y-auto px-4">
      <div className="mx-auto flex w-full max-w-7xl flex-col gap-4 py-4">
        {messages.length === 0 && (
          <div className="rounded-3xl border border-border/80 bg-background/80 px-6 py-10 text-center shadow-sm">
            <p className="text-sm font-medium">Start a conversation</p>
            <p className="mt-2 text-sm text-muted-foreground">
              Ask a question below to get started.
            </p>
          </div>
        )}

        {
          messages.map((message, index) => {
            if (message.role === USER_ROLE) {
              if (index === lastUserMessageIdx)
                return <div ref={lastUserMessageRef} key={`${index}-${message.role}-${message.content}`}>
                  <UserMessage message={message.content} />
                </div>

              return <UserMessage key={`${index}-${message.role}-${message.content}`} message={message.content} />
            }

            return <ChatMessage key={`${index}-${message.role}-${message.content}`} message={message} />
          })
        }

        {mutation.isPending && <LoadingMessage />}

        {submitError && (
          <div className="rounded-2xl border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive shadow-sm">
            {submitError.error}
          </div>
        )}
      </div>
      <div ref={scrollAnchorRef} />
    </section>

    <div className="border-t border-border/70 bg-background/95 px-4 py-4 backdrop-blur">
      <form className="mx-auto w-full max-w-7xl" onSubmit={handleSubmit}>
        <PromptInput
          name="prompt"
          value={prompt}
          onValueChange={setPrompt}
          reasoning={reasoning}
          setReasoning={setReasoning}
          disabled={mutation.isPending}
          isSubmitting={mutation.isPending}
        />
      </form>
    </div>
  </Layout>
}

function UnauthenticatedApp() {
  return (
    <Layout>
      <div className="flex h-full w-full items-center justify-center">
        <MsalAuthenticationTemplate
          interactionType={InteractionType.Redirect}
          authenticationRequest={authRequest}
          errorComponent={(error) => <p>Error: {error.error?.message}</p>}
          loadingComponent={() => <p>Authentication in progress...</p>}
        />
      </div>
    </Layout>
  )
}

function App() {
  return <>
    <AuthenticatedTemplate>
      <AuthenticatedApp />
    </AuthenticatedTemplate>
    <UnauthenticatedTemplate>
      <UnauthenticatedApp />
    </UnauthenticatedTemplate>
  </>
}

export default App
